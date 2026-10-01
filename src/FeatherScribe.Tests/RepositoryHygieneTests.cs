using System.Text.RegularExpressions;

namespace FeatherScribe.Tests;

// Regression guard for the public repository: no development-machine paths, e-mail
// addresses, or secret-looking tokens in publishable text files, and the local-only
// folders stay gitignored. The patterns below do not match their own source text,
// so this file is scanned like every other file.
public sealed class RepositoryHygieneTests
{
    private static readonly string[] TextExtensions =
    [
        ".cs", ".xaml", ".md", ".json", ".ps1", ".sh", ".cmd", ".bat", ".csproj", ".slnx", ".txt",
        ".editorconfig", ".gitattributes", ".gitignore",
    ];

    private static readonly string[] TextFileNames = ["LICENSE"];

    // Excluded at any depth.
    private static readonly string[] ExcludedDirectoryNames =
    [
        ".git", "bin", "obj", ".vs", ".idea", "TestResults",
    ];

    // Excluded relative to the repository root.
    private static readonly string[] ExcludedRootDirectories =
    [
        "local", "reports", ".claude", ".agents", "docs/work", "doc/work", "samples/audio",
    ];

    private static readonly (string Name, Regex Pattern)[] ForbiddenPatterns =
    [
        ("Windows user/dev path", new Regex(@"(?i)\b[A-Z]:[\\/](Users|Develop|works)[\\/]", RegexOptions.CultureInvariant)),
        ("AppData path", new Regex(@"(?i)[\\/]AppData[\\/]", RegexOptions.CultureInvariant)),
        ("secret token (sk-)", new Regex(@"sk-[A-Za-z0-9]{20,}", RegexOptions.CultureInvariant)),
        ("secret token (ghp_)", new Regex(@"ghp_[A-Za-z0-9]{20,}", RegexOptions.CultureInvariant)),
        ("secret token (AKIA)", new Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.CultureInvariant)),
        ("private key block", new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)),
    ];

    private static readonly Regex EmailPattern =
        new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant);

    private static readonly string[] RequiredGitIgnoreEntries =
    [
        "local/", "reports/*", "config/appsettings.local.json", "docs/work/", ".claude/",
    ];

    [Fact]
    public void PublishableTextFiles_ContainNoMachinePathsEmailsOrSecrets()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            // Repository root not available (e.g. tests run from a copied output folder).
            return;
        }

        var files = EnumerateTextFiles(root).ToArray();
        Assert.NotEmpty(files);

        var hits = new List<string>();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            hits.AddRange(ScanText(relativePath, File.ReadAllText(file)));
        }

        Assert.True(hits.Count == 0, "Repository hygiene violations:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public void GitIgnore_ExcludesLocalOnlyFolders()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            return;
        }

        var entries = File.ReadAllLines(Path.Combine(root, ".gitignore"))
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(RequiredGitIgnoreEntries, entry => Assert.Contains(entry, entries));
    }

    internal static IReadOnlyList<string> ScanText(string relativePath, string content)
    {
        var hits = new List<string>();
        var lines = content.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var location = $"{relativePath}:{index + 1}";

            foreach (var (name, pattern) in ForbiddenPatterns)
            {
                if (pattern.IsMatch(line))
                {
                    hits.Add($"{location}: {name}");
                }
            }

            foreach (Match match in EmailPattern.Matches(line))
            {
                if (!IsAllowedEmail(match.Value))
                {
                    hits.Add($"{location}: e-mail address");
                }
            }
        }

        return hits;
    }

    private static bool IsAllowedEmail(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        var local = address[..at];
        var domain = address[(at + 1)..].ToLowerInvariant();

        return local.Equals("noreply", StringComparison.OrdinalIgnoreCase)
            || domain == "users.noreply.github.com"
            || domain is "example.com" or "example.org"
            || domain.EndsWith(".example.com", StringComparison.Ordinal)
            || domain.EndsWith(".example.org", StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateTextFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var subdirectory in Directory.GetDirectories(directory))
            {
                if (!IsExcludedDirectory(root, subdirectory))
                {
                    pending.Push(subdirectory);
                }
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                var fileName = Path.GetFileName(file);
                if (TextFileNames.Contains(fileName, StringComparer.Ordinal)
                    || TextExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsExcludedDirectory(string root, string directory)
    {
        if (ExcludedDirectoryNames.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var relativePath = Path.GetRelativePath(root, directory).Replace('\\', '/');
        return ExcludedRootDirectories.Contains(relativePath, StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FeatherScribe.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
