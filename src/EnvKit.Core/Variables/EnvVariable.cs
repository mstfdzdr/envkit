namespace EnvKit.Core.Variables;

public enum EnvScope
{
    User,
    Machine,
}

/// <summary>
/// How a value is stored. <see cref="Expandable"/> (REG_EXPAND_SZ) keeps <c>%VAR%</c>
/// references live; writing it back as a plain string silently breaks them.
/// </summary>
public enum EnvValueKind
{
    /// <summary>REG_SZ</summary>
    Literal,

    /// <summary>REG_EXPAND_SZ</summary>
    Expandable,
}

public sealed record EnvVariable
{
    /// <summary>32,767 characters including the terminating null.</summary>
    public const int MaxValueLength = 32_766;

    /// <summary>Windows treats variable names case-insensitively.</summary>
    public static StringComparer NameComparer => StringComparer.OrdinalIgnoreCase;

    public EnvVariable(string name, string value, EnvScope scope, EnvValueKind kind = EnvValueKind.Literal)
    {
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(value);

        Name = name;
        Value = value;
        Scope = scope;
        Kind = kind;
    }

    public string Name { get; }

    public string Value { get; }

    public EnvScope Scope { get; }

    public EnvValueKind Kind { get; }

    /// <summary>
    /// The registry can hold longer values than a process environment accepts. Such values are
    /// still read (so they can be reported) but must not be written.
    /// </summary>
    public bool ExceedsMaxLength => Value.Length > MaxValueLength;

    public void EnsureWritable()
    {
        if (ExceedsMaxLength)
        {
            throw new ArgumentException(
                $"{Name} is {Value.Length} characters; the maximum is {MaxValueLength}.");
        }
    }

    /// <summary>Never includes the value, which may be a secret; this ends up in logs and UI automation names.</summary>
    public override string ToString() => $@"{Scope}\{Name}";

    public static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('=') || name.Contains('\0'))
        {
            throw new ArgumentException("Variable names cannot contain '=' or null characters.", nameof(name));
        }
    }
}
