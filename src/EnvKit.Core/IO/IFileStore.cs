namespace EnvKit.Core.IO;

/// <summary>Whole-file reads and writes for the files EnvKit edits (.env, secrets.json).</summary>
public interface IFileStore
{
    /// <returns><see langword="null"/> if the file does not exist.</returns>
    byte[]? Read(string path);

    /// <summary>Replaces the file atomically, creating its folder if needed.</summary>
    void Write(string path, byte[] content);

    /// <returns><see langword="false"/> if it did not exist.</returns>
    bool Delete(string path);
}

public sealed class PhysicalFileStore : IFileStore
{
    public static PhysicalFileStore Instance { get; } = new();

    public byte[]? Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    public void Write(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        // Same folder, so the final step is a rename on the same volume: readers see the old
        // file or the new one, never a half-written one.
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.envkit-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temp, content);
        try
        {
            if (File.Exists(path))
            {
                File.Replace(temp, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public bool Delete(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }
}
