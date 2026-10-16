using System.ComponentModel;
using System.Runtime.InteropServices;
using EnvKit.Core.Credentials;

namespace EnvKit.Windows.Credentials;

/// <summary>The current user's Windows Credential Manager (CredEnumerate/CredRead/CredWrite/CredDelete).</summary>
public sealed partial class WindowsCredentialStore : ICredentialStore
{
    private const int ErrorNotFound = 1168;
    private const int MaxCommentLength = 256;
    private const int MaxUserNameLength = 513;

    public IReadOnlyList<Credential> List(string? filter = null)
    {
        // Not CRED_ENUMERATE_ALL_CREDENTIALS: it rewrites target names as "LegacyGeneric:target=…",
        // which CredRead and CredDelete do not accept. A null filter already lists everything.
        if (!CredEnumerate(filter, 0, out var count, out var buffer))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? [] : throw new Win32Exception(error);
        }

        try
        {
            var credentials = new List<Credential>((int)count);
            for (var i = 0; i < count; i++)
            {
                credentials.Add(FromNative(Marshal.ReadIntPtr(buffer, i * IntPtr.Size)));
            }

            return credentials;
        }
        finally
        {
            CredFree(buffer);
        }
    }

    public Credential? Get(CredentialKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!CredRead(key.TargetName, (uint)key.Kind, 0, out var buffer))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }

        try
        {
            return FromNative(buffer);
        }
        finally
        {
            CredFree(buffer);
        }
    }

    public void Write(CredentialKey key, CredentialState state)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(state);
        Validate(key, state);

        var allocations = new List<nint>();
        var secret = state.Secret ?? [];
        var blob = Marshal.AllocHGlobal(Math.Max(secret.Length, 1));
        try
        {
            Marshal.Copy(secret, 0, blob, secret.Length);
            var native = new NativeCredential
            {
                Type = (uint)key.Kind,
                TargetName = String(key.TargetName, allocations),
                Comment = String(state.Comment, allocations),
                CredentialBlobSize = (uint)secret.Length,
                CredentialBlob = blob,
                Persist = (uint)state.Persistence,
                AttributeCount = (uint)state.AttributeList.Count,
                Attributes = Attributes(state.AttributeList, allocations),
                TargetAlias = String(state.TargetAlias, allocations),
                UserName = String(state.UserName, allocations),
            };

            if (!CredWrite(in native, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            // Do not leave the secret in freed memory.
            for (var i = 0; i < secret.Length; i++)
            {
                Marshal.WriteByte(blob, i, 0);
            }

            Marshal.FreeHGlobal(blob);
            allocations.ForEach(Marshal.FreeHGlobal);
        }
    }

    public bool Delete(CredentialKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (CredDelete(key.TargetName, (uint)key.Kind, 0))
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        return error == ErrorNotFound ? false : throw new Win32Exception(error);
    }

    private static void Validate(CredentialKey key, CredentialState state)
    {
        // Domain credentials belong to the logon session; their secret cannot be read back, so a
        // rewrite could not preserve it. They can be deleted, not edited.
        if (key.Kind != CredentialKind.Generic)
        {
            throw new NotSupportedException($"Only generic credentials can be written, not {key.Kind}.");
        }

        if ((state.Secret?.Length ?? 0) > CredentialState.MaxSecretBytes)
        {
            throw new ArgumentException($"The secret is {state.Secret!.Length} bytes; the limit is {CredentialState.MaxSecretBytes}.");
        }

        if ((state.Comment?.Length ?? 0) > MaxCommentLength || (state.UserName?.Length ?? 0) > MaxUserNameLength)
        {
            throw new ArgumentException($"The comment is limited to {MaxCommentLength} and the user name to {MaxUserNameLength} characters.");
        }
    }

    private static Credential FromNative(nint pointer)
    {
        var native = Marshal.PtrToStructure<NativeCredential>(pointer);
        var kind = (CredentialKind)native.Type;

        // Windows returns no blob for domain credentials to anyone but the security package.
        byte[]? secret = null;
        if (kind is CredentialKind.Generic or CredentialKind.GenericCertificate)
        {
            secret = new byte[native.CredentialBlobSize];
            if (secret.Length > 0)
            {
                Marshal.Copy(native.CredentialBlob, secret, 0, secret.Length);
            }
        }

        var attributes = new List<CredentialAttributeEntry>((int)native.AttributeCount);
        var attributeSize = Marshal.SizeOf<NativeAttribute>();
        for (var i = 0; i < native.AttributeCount; i++)
        {
            var attribute = Marshal.PtrToStructure<NativeAttribute>(native.Attributes + (i * attributeSize));
            var value = new byte[attribute.ValueSize];
            if (value.Length > 0)
            {
                Marshal.Copy(attribute.Value, value, 0, value.Length);
            }

            attributes.Add(new CredentialAttributeEntry(Marshal.PtrToStringUni(attribute.Keyword) ?? string.Empty, attribute.Flags, value));
        }

        var state = new CredentialState(
            Marshal.PtrToStringUni(native.UserName),
            secret,
            (CredentialPersistence)native.Persist,
            Marshal.PtrToStringUni(native.Comment),
            Marshal.PtrToStringUni(native.TargetAlias),
            attributes.Count == 0 ? null : attributes);
        var lastWritten = DateTimeOffset.FromFileTime(((long)native.LastWrittenHigh << 32) | native.LastWrittenLow);
        return new Credential(new CredentialKey(Marshal.PtrToStringUni(native.TargetName)!, kind), state, lastWritten);
    }

    private static nint String(string? value, List<nint> allocations)
    {
        if (value is null)
        {
            return 0;
        }

        var pointer = Marshal.StringToHGlobalUni(value);
        allocations.Add(pointer);
        return pointer;
    }

    private static nint Attributes(IReadOnlyList<CredentialAttributeEntry> attributes, List<nint> allocations)
    {
        if (attributes.Count == 0)
        {
            return 0;
        }

        var size = Marshal.SizeOf<NativeAttribute>();
        var array = Marshal.AllocHGlobal(size * attributes.Count);
        allocations.Add(array);
        for (var i = 0; i < attributes.Count; i++)
        {
            var value = Marshal.AllocHGlobal(Math.Max(attributes[i].Value.Length, 1));
            allocations.Add(value);
            Marshal.Copy(attributes[i].Value, 0, value, attributes[i].Value.Length);
            var native = new NativeAttribute
            {
                Keyword = String(attributes[i].Keyword, allocations),
                Flags = attributes[i].Flags,
                ValueSize = (uint)attributes[i].Value.Length,
                Value = value,
            };
            Marshal.StructureToPtr(native, array + (i * size), fDeleteOld: false);
        }

        return array;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAttribute
    {
        public nint Keyword;
        public uint Flags;
        public uint ValueSize;
        public nint Value;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredEnumerateW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerate(string? filter, uint flags, out uint count, out nint credentials);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string targetName, uint type, uint flags, out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(in NativeCredential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string targetName, uint type, uint flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(nint buffer);
}
