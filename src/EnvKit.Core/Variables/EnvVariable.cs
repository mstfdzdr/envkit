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
        if (value.Length > MaxValueLength)
        {
            throw new ArgumentException(
                $"Value is {value.Length} characters; the maximum is {MaxValueLength}.", nameof(value));
        }

        Name = name;
        Value = value;
        Scope = scope;
        Kind = kind;
    }

    public string Name { get; }

    public string Value { get; }

    public EnvScope Scope { get; }

    public EnvValueKind Kind { get; }

    public static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('=') || name.Contains('\0'))
        {
            throw new ArgumentException("Variable names cannot contain '=' or null characters.", nameof(name));
        }
    }
}
