using System.Collections;
using System.Diagnostics;

namespace EnvKit.Cli;

internal static class StandardInput
{
    /// <summary>
    /// One line of piped input. Windows PowerShell prefixes piped text with a UTF-8 byte order
    /// mark that Console.In passes through; left in, it would become part of the secret.
    /// </summary>
    public static string? ReadLine(TextReader input) => input.ReadLine()?.TrimStart('﻿');
}

public interface IProcessRunner
{
    /// <summary>Runs to completion with the given environment block; returns the exit code.</summary>
    int Run(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment);
}

internal sealed class ProcessRunner : IProcessRunner
{
    public static IReadOnlyDictionary<string, string> CurrentEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        return environment;
    }

    public int Run(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        // Ctrl+C reaches the child too; stay alive until it exits so its exit code is returned.
        ConsoleCancelEventHandler keepRunning = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += keepRunning;
        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= keepRunning;
        }
    }
}
