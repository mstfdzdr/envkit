using System.Security.Cryptography;
using System.Text.Json;
using EnvKit.Core.Plans;
using EnvKit.Core.Security;

namespace EnvKit.Core.Snapshots;

/// <summary>
/// One encrypted file per snapshot. Variables can hold secrets (tokens pasted into env vars
/// are common), so nothing is written in plain text.
/// </summary>
public sealed class FileSnapshotStore(string directory, ISecretProtector protector, int retain = FileSnapshotStore.DefaultRetain)
    : ISnapshotStore
{
    public const int DefaultRetain = 100;
    private const string Extension = ".snapshot";

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnvKit", "snapshots");

    public void Save(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Directory.CreateDirectory(directory);

        var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(snapshot, PlanJson.Options));
        var path = PathFor(snapshot.Id);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);

        Prune();
    }

    public IReadOnlyList<SnapshotInfo> List()
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var infos = new List<SnapshotInfo>();
        foreach (var file in Directory.EnumerateFiles(directory, "*" + Extension))
        {
            if (TryRead(file) is { } snapshot)
            {
                infos.Add(snapshot.Info);
            }
        }

        return [.. infos.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id, StringComparer.Ordinal)];
    }

    public Snapshot Load(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
        {
            throw new KeyNotFoundException($"No snapshot '{id}'.");
        }

        return Read(path);
    }

    private string PathFor(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // Ids come from the CLI; keep them from escaping the directory.
        if (id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        {
            throw new ArgumentException($"Invalid snapshot id '{id}'.", nameof(id));
        }

        return Path.Combine(directory, id + Extension);
    }

    private Snapshot Read(string path) =>
        JsonSerializer.Deserialize<Snapshot>(protector.Unprotect(File.ReadAllBytes(path)), PlanJson.Options)
        ?? throw new JsonException($"Empty snapshot {path}.");

    private Snapshot? TryRead(string path)
    {
        try
        {
            return Read(path);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    private void Prune()
    {
        // Ids start with a sortable timestamp, so the name order is the age order.
        var stale = Directory.EnumerateFiles(directory, "*" + Extension)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(retain);
        foreach (var file in stale)
        {
            File.Delete(file);
        }
    }
}
