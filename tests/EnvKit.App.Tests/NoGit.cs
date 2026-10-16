using EnvKit.Core.DotEnv;

namespace EnvKit.App.Tests;

internal sealed class NoGit : IGitInspector
{
    public (bool Tracked, bool Ignored)? Inspect(string path) => null;
}