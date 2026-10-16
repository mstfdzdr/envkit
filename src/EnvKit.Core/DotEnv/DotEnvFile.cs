using System.Text;

namespace EnvKit.Core.DotEnv;

/// <summary>A .env document with the byte-level details (encoding, byte order mark) it was read with.</summary>
public sealed class DotEnvFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Encoding _encoding;
    private readonly byte[] _preamble;

    private DotEnvFile(DotEnvDocument document, Encoding encoding, byte[] preamble)
    {
        Document = document;
        _encoding = encoding;
        _preamble = preamble;
    }

    public DotEnvDocument Document { get; }

    public static DotEnvFile Empty() => new(DotEnvDocument.Parse(string.Empty), StrictUtf8, []);

    /// <exception cref="InvalidDataException">Not valid UTF-8 (or UTF-16 with a byte order mark).</exception>
    public static DotEnvFile Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var (encoding, preamble) = bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => ((Encoding)StrictUtf8, new byte[] { 0xEF, 0xBB, 0xBF }),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), new byte[] { 0xFF, 0xFE }),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), new byte[] { 0xFE, 0xFF }),
            _ => ((Encoding)StrictUtf8, Array.Empty<byte>()),
        };

        string text;
        try
        {
            text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
        }
        catch (DecoderFallbackException ex)
        {
            // Rewriting would silently replace the bad bytes, so such files are read-only.
            throw new InvalidDataException("The file is not valid UTF-8, so EnvKit will not edit it.", ex);
        }

        return new DotEnvFile(DotEnvDocument.Parse(text), encoding, preamble);
    }

    public byte[] ToBytes() => [.. _preamble, .. _encoding.GetBytes(Document.ToText())];
}
