using EnvKit.Core.IO;

namespace EnvKit.Testing;

public sealed class FakeFileSystem : IFileSystem
{
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem WithDirectories(params string[] paths)
    {
        _directories.UnionWith(paths);
        return this;
    }

    /// <summary>Adds the files and their parent directories.</summary>
    public FakeFileSystem WithFiles(params string[] paths)
    {
        _files.UnionWith(paths);
        _directories.UnionWith(paths.Select(p => p[..p.LastIndexOf('\\')]));
        return this;
    }

    public bool DirectoryExists(string path) => _directories.Contains(path);

    public bool FileExists(string path) => _files.Contains(path);
}
