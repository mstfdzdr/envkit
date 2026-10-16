namespace EnvKit.Cli.Tests;

internal sealed class RecordingProcessRunner : IProcessRunner
{
    public string? FileName { get; private set; }

    public IReadOnlyList<string> Arguments { get; private set; } = [];

    public IReadOnlyDictionary<string, string> Environment { get; private set; } = new Dictionary<string, string>();

    public int ExitCode { get; init; }

    public int Run(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        FileName = fileName;
        Arguments = arguments;
        Environment = environment;
        return ExitCode;
    }
}
