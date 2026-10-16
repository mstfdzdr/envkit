namespace EnvKit.Core.IO;

public interface IFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public static PhysicalFileSystem Instance { get; } = new();

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);
}
