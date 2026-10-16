using EnvKit.Core.IO;

namespace EnvKit.Testing;

public sealed class InMemoryFileStore : IFileStore
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Writes to these paths throw, to exercise rollback.</summary>
    public HashSet<string> FailOnWrite { get; } = new(StringComparer.OrdinalIgnoreCase);

    public byte[]? Read(string path) => _files.TryGetValue(Path.GetFullPath(path), out var content) ? [.. content] : null;

    public void Write(string path, byte[] content)
    {
        if (FailOnWrite.Contains(Path.GetFullPath(path)))
        {
            throw new IOException($"Simulated failure writing {path}.");
        }

        _files[Path.GetFullPath(path)] = [.. content];
    }

    public bool Delete(string path) => _files.Remove(Path.GetFullPath(path));

    public void Seed(string path, string text) => _files[Path.GetFullPath(path)] = System.Text.Encoding.UTF8.GetBytes(text);

    public string? Text(string path) => Read(path) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;
}
