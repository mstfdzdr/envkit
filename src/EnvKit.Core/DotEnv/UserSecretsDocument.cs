using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EnvKit.Core.DotEnv;

/// <summary>
/// A .NET user-secrets file (<c>secrets.json</c>). <c>dotnet user-secrets set</c> writes flat
/// keys such as <c>ConnectionStrings:Default</c>; hand-edited files may nest objects. Both are
/// read as configuration keys (case-insensitive, ':' separated), and edits keep whichever
/// shape the key already has. Comments are not preserved: the file is rewritten as JSON.
/// </summary>
public sealed class UserSecretsDocument
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly JsonObject _root;

    private UserSecretsDocument(JsonObject root) => _root = root;

    public static StringComparer KeyComparer => StringComparer.OrdinalIgnoreCase;

    /// <param name="bytes"><see langword="null"/> for a project that has no secrets file yet.</param>
    /// <exception cref="JsonException">Not a JSON object.</exception>
    public static UserSecretsDocument Parse(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return new UserSecretsDocument([]);
        }

        var node = JsonNode.Parse(bytes, documentOptions: ReadOptions) as JsonObject
            ?? throw new JsonException("secrets.json must contain a JSON object.");
        return new UserSecretsDocument(node);
    }

    /// <summary>Flattened configuration keys and their values, in file order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Values()
    {
        var values = new List<KeyValuePair<string, string>>();
        Flatten(_root, prefix: null, values);
        return values;
    }

    public string? Get(string key) => Values().LastOrDefault(v => KeyComparer.Equals(v.Key, key)).Value;

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (FindLeaf(key) is { } leaf)
        {
            leaf.Parent[leaf.Name] = value;
            return;
        }

        // New keys are added flat, as dotnet user-secrets does.
        _root[key] = value;
    }

    public bool Remove(string key)
    {
        if (FindLeaf(key) is not { } leaf)
        {
            return false;
        }

        leaf.Parent.Remove(leaf.Name);
        return true;
    }

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(_root.ToJsonString(WriteOptions));

    private (JsonObject Parent, string Name)? FindLeaf(string key)
    {
        // The flat spelling first, then the nested path.
        if (Property(_root, key) is { } flat && _root[flat] is not JsonObject)
        {
            return (_root, flat);
        }

        var parts = key.Split(':');
        var current = _root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (Property(current, parts[i]) is not { } name || current[name] is not JsonObject child)
            {
                return null;
            }

            current = child;
        }

        return Property(current, parts[^1]) is { } last && current[last] is not JsonObject ? (current, last) : null;
    }

    private static string? Property(JsonObject node, string name) =>
        node.Select(p => p.Key).FirstOrDefault(k => KeyComparer.Equals(k, name));

    private static void Flatten(JsonNode? node, string? prefix, List<KeyValuePair<string, string>> values)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    Flatten(child, prefix is null ? name : $"{prefix}:{name}", values);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    Flatten(array[i], $"{prefix}:{i}", values);
                }

                break;
            case JsonValue value when prefix is not null:
                values.Add(new(prefix, value.TryGetValue<string>(out var text) ? text : value.ToJsonString()));
                break;
            case null when prefix is not null:
                values.Add(new(prefix, string.Empty));
                break;
        }
    }
}
