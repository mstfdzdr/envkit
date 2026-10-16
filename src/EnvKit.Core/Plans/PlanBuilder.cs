using EnvKit.Core.Credentials;
using EnvKit.Core.Paths;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Plans;

/// <summary>
/// Collects edits against the current store state. Repeated edits to one variable collapse
/// into a single change, and edits that end where they started disappear.
/// </summary>
/// <param name="credentials">Required only for credential edits and for restoring snapshots that contain credentials.</param>
public sealed class PlanBuilder(IEnvStore store, ICredentialStore? credentials = null, TimeProvider? timeProvider = null)
{
    private readonly List<Pending> _pending = [];
    private readonly List<PendingCredential> _pendingCredentials = [];

    /// <summary>Creates or replaces a generic credential.</summary>
    public PlanBuilder SetCredential(CredentialKey key, CredentialState state)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(state);
        if (key.Kind != CredentialKind.Generic)
        {
            throw new NotSupportedException($"Only generic credentials can be written, not {key.Kind}.");
        }

        if ((state.Secret?.Length ?? 0) > CredentialState.MaxSecretBytes)
        {
            throw new ArgumentException($"The secret is {state.Secret!.Length} bytes; the limit is {CredentialState.MaxSecretBytes}.");
        }

        TrackCredential(key).After = state;
        return this;
    }

    public PlanBuilder DeleteCredential(CredentialKey key)
    {
        TrackCredential(key).After = null;
        return this;
    }

    public PlanBuilder Set(string name, EnvScope scope, string value, EnvValueKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var pending = Track(name, scope);
        var resolvedKind = kind ?? InferKind(pending.After, value);
        new EnvVariable(pending.Name, value, scope, resolvedKind).EnsureWritable();
        pending.After = new VariableValue(value, resolvedKind);
        return this;
    }

    public PlanBuilder Delete(string name, EnvScope scope)
    {
        Track(name, scope).After = null;
        return this;
    }

    public IReadOnlyList<string> GetPath(EnvScope scope) =>
        Track(PathAnalyzer.VariableName, scope).After is { } value ? PathList.Split(value.Value) : [];

    public PlanBuilder SetPath(EnvScope scope, IEnumerable<string> entries) =>
        Set(PathAnalyzer.VariableName, scope, PathList.Join(entries));

    /// <returns><see langword="false"/> if the directory is already in that scope's PATH.</returns>
    public bool AddPathEntry(string directory, EnvScope scope, bool prepend = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var entries = GetPath(scope).ToList();
        if (entries.Any(e => SameDirectory(e, directory)))
        {
            return false;
        }

        entries.Insert(prepend ? 0 : entries.Count, directory);
        SetPath(scope, entries);
        return true;
    }

    /// <returns>How many entries were removed.</returns>
    public int RemovePathEntry(string directory, EnvScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var entries = GetPath(scope).ToList();
        var removed = entries.RemoveAll(e => SameDirectory(e, directory));
        if (removed > 0)
        {
            SetPath(scope, entries);
        }

        return removed;
    }

    public PlanBuilder ApplyCleanup(IReadOnlyDictionary<EnvScope, string> cleanedPaths)
    {
        ArgumentNullException.ThrowIfNull(cleanedPaths);
        foreach (var (scope, value) in cleanedPaths)
        {
            Set(PathAnalyzer.VariableName, scope, value);
        }

        return this;
    }

    /// <summary>Returns the store to the snapshot's state, including deleting variables created since.</summary>
    public PlanBuilder Restore(Snapshot snapshot, EnvScope? onlyScope = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var scope in new[] { EnvScope.Machine, EnvScope.User })
        {
            if (onlyScope is { } only && only != scope)
            {
                continue;
            }

            var target = snapshot.Variables
                .Where(v => v.Scope == scope)
                .ToDictionary(v => v.Name, EnvVariable.NameComparer);

            foreach (var current in store.GetAll(scope))
            {
                if (!target.ContainsKey(current.Name))
                {
                    Delete(current.Name, scope);
                }
            }

            foreach (var variable in target.Values)
            {
                Set(variable.Name, scope, variable.Value, variable.Kind);
            }
        }

        // Credentials are restored only when restoring everything: they are not scoped.
        if (onlyScope is null && credentials is not null)
        {
            foreach (var credential in snapshot.CredentialList)
            {
                if (credential.State is null)
                {
                    DeleteCredential(credential.Key);
                }
                else if (credential.State.Secret is not null)
                {
                    SetCredential(credential.Key, credential.State);
                }

                // A domain credential without a readable secret cannot be recreated; it is skipped.
            }
        }

        return this;
    }

    public ChangePlan Build(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        var changes = _pending
            .Where(p => p.Before != p.After)
            .Select(PlanChange (p) => new VariableChange(p.Scope, p.Name, p.Before, p.After))
            .Concat(_pendingCredentials
                .Where(p => p.Before != p.After)
                .Select(PlanChange (p) => new CredentialChange(p.Key, p.Before, p.After)))
            .ToList();
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        return new ChangePlan(Guid.NewGuid(), description, now, changes);
    }

    private Pending Track(string name, EnvScope scope)
    {
        EnvVariable.ValidateName(name);
        var existing = _pending.Find(p => p.Scope == scope && EnvVariable.NameComparer.Equals(p.Name, name));
        if (existing is not null)
        {
            return existing;
        }

        var stored = store.Get(name, scope);

        // Keep the stored spelling ("Path", not "PATH") so the change does not look like a rename.
        var before = stored is null ? null : new VariableValue(stored.Value, stored.Kind);
        var pending = new Pending(stored?.Name ?? name, scope, before);
        _pending.Add(pending);
        return pending;
    }

    /// <summary>
    /// Keep the current kind, except that a value with references becomes expandable;
    /// that also repairs a REG_SZ PATH whose references Windows was ignoring.
    /// </summary>
    private static EnvValueKind InferKind(VariableValue? current, string value) =>
        EnvExpander.HasReferences(value) ? EnvValueKind.Expandable : current?.Kind ?? EnvValueKind.Literal;

    private static bool SameDirectory(string a, string b) =>
        string.Equals(PathList.Normalize(a), PathList.Normalize(b), StringComparison.OrdinalIgnoreCase);

    private PendingCredential TrackCredential(CredentialKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var existing = _pendingCredentials.Find(p => p.Key == key);
        if (existing is not null)
        {
            return existing;
        }

        var credentialStore = credentials ?? throw new InvalidOperationException("No credential store was given.");
        var stored = credentialStore.Get(key);

        // Keep the stored spelling of the target, as for variable names.
        var pending = new PendingCredential(stored?.Key ?? key, stored?.State);
        _pendingCredentials.Add(pending);
        return pending;
    }

    private sealed class PendingCredential(CredentialKey key, CredentialState? before)
    {
        public CredentialKey Key { get; } = key;

        public CredentialState? Before { get; } = before;

        public CredentialState? After { get; set; } = before;
    }

    private sealed class Pending(string name, EnvScope scope, VariableValue? before)
    {
        public string Name { get; } = name;

        public EnvScope Scope { get; } = scope;

        public VariableValue? Before { get; } = before;

        public VariableValue? After { get; set; } = before;
    }
}
