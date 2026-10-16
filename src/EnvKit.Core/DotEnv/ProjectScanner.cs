using System.Diagnostics;
using System.Xml;
using EnvKit.Core.Plans;

namespace EnvKit.Core.DotEnv;

public enum SecretFileWarning
{
    /// <summary>Tracked by git: its contents may already be in the history.</summary>
    Committed,

    /// <summary>In a git repository but not ignored: <c>git add .</c> would commit it.</summary>
    NotIgnored,
}

/// <param name="Project">For user secrets, the .csproj that declares the id.</param>
/// <param name="IsTemplate">.env.example and the like: meant to be committed, without real values.</param>
public sealed record ProjectSecretFile(
    string Path,
    FileFormat Format,
    string? Project = null,
    string? UserSecretsId = null,
    bool IsTemplate = false,
    IReadOnlyList<SecretFileWarning>? Warnings = null)
{
    public IReadOnlyList<SecretFileWarning> WarningList => Warnings ?? [];
}

public interface IGitInspector
{
    /// <returns><see langword="null"/> when the file is not in a repository or git is unavailable.</returns>
    (bool Tracked, bool Ignored)? Inspect(string path);
}

/// <summary>Asks the git command line, so .gitignore rules are applied exactly as git applies them.</summary>
public sealed class GitInspector : IGitInspector
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public (bool Tracked, bool Ignored)? Inspect(string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path)!;
        var name = System.IO.Path.GetFileName(path);

        // check-ignore: 0 ignored, 1 not ignored, 128 not a repository.
        var ignored = Git(directory, "check-ignore", "-q", "--", name);
        if (ignored is not (0 or 1))
        {
            return null;
        }

        var tracked = Git(directory, "ls-files", "--error-unmatch", "--", name) == 0;
        return (tracked, ignored == 0);
    }

    private static int? Git(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // git is not installed.
            return null;
        }
    }
}

public static class ProjectScanner
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "node_modules", "bin", "obj", "packages", "dist", "build", "target",
        "venv", ".venv", "__pycache__", ".next", ".nuxt", "vendor",
    };

    private static readonly string[] TemplateSuffixes = [".example", ".sample", ".template", ".dist", ".defaults"];

    public static string UserSecretsRoot { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets");

    /// <summary>Finds .env files and user-secrets projects under <paramref name="root"/>.</summary>
    public static IReadOnlyList<ProjectSecretFile> Scan(string root, IGitInspector? git = null, int maxDepth = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var found = new List<ProjectSecretFile>();
        Walk(System.IO.Path.GetFullPath(root), 0, maxDepth, found);
        return [.. found
            .Select(f => git is null || f.Format != FileFormat.DotEnv || f.IsTemplate ? f : f with { Warnings = Warnings(git, f.Path) })
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
    }

    public static bool IsDotEnvName(string fileName) =>
        fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase);

    public static bool IsTemplateName(string fileName) =>
        TemplateSuffixes.Any(s => fileName.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The secrets.json path for an id, or <see langword="null"/> if the id could escape the
    /// UserSecrets folder: it comes from a project file, which may not be trustworthy.
    /// </summary>
    public static string? UserSecretsPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Contains("..", StringComparison.Ordinal) || id.IndexOfAny([.. System.IO.Path.GetInvalidFileNameChars(), '/', '\\']) >= 0)
        {
            return null;
        }

        return System.IO.Path.Combine(UserSecretsRoot, id, "secrets.json");
    }

    /// <summary>The UserSecretsId declared in a project file, if any.</summary>
    public static string? ReadUserSecretsId(string projectPath)
    {
        try
        {
            var document = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(projectPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
            return document.GetElementsByTagName("UserSecretsId").Cast<XmlNode>().FirstOrDefault()?.InnerText.Trim() is { Length: > 0 } id ? id : null;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Walk(string directory, int depth, int maxDepth, List<ProjectSecretFile> found)
    {
        IEnumerable<string> files;
        IEnumerable<string> subdirectories;
        try
        {
            files = Directory.EnumerateFiles(directory).ToList();
            subdirectories = Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var file in files)
        {
            var name = System.IO.Path.GetFileName(file);
            if (IsDotEnvName(name))
            {
                found.Add(new ProjectSecretFile(file, FileFormat.DotEnv, IsTemplate: IsTemplateName(name)));
            }
            else if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                && ReadUserSecretsId(file) is { } id
                && UserSecretsPath(id) is { } secrets)
            {
                found.Add(new ProjectSecretFile(secrets, FileFormat.UserSecrets, file, id));
            }
        }

        if (depth >= maxDepth)
        {
            return;
        }

        foreach (var subdirectory in subdirectories)
        {
            var name = System.IO.Path.GetFileName(subdirectory);

            // Reparse points (junctions, symlinks) could loop or lead outside the tree.
            if (!SkippedFolders.Contains(name) && !new DirectoryInfo(subdirectory).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Walk(subdirectory, depth + 1, maxDepth, found);
            }
        }
    }

    private static List<SecretFileWarning> Warnings(IGitInspector git, string path)
    {
        var warnings = new List<SecretFileWarning>();
        if (git.Inspect(path) is { } status)
        {
            if (status.Tracked)
            {
                warnings.Add(SecretFileWarning.Committed);
            }
            else if (!status.Ignored)
            {
                warnings.Add(SecretFileWarning.NotIgnored);
            }
        }

        return warnings;
    }
}
