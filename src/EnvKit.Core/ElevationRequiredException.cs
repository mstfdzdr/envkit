namespace EnvKit.Core;

/// <summary>
/// The operation touches a location the current process cannot write without administrator rights.
/// Callers route such changes through the elevated apply-plan flow instead of failing.
/// </summary>
public sealed class ElevationRequiredException : Exception
{
    public ElevationRequiredException()
        : base("Administrator rights are required for this change.")
    {
    }

    public ElevationRequiredException(string message)
        : base(message)
    {
    }

    public ElevationRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
