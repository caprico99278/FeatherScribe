using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FeatherScribe.Tests;

// Contract for the double-click launcher FeatherScribe.cmd at the repository root:
// cmd.exe needs ASCII and CRLF, paths stay relative, and the exe path must match the
// App project's TargetFramework so the launcher starts what it just built.
public sealed class LauncherContractTests
{
    private const string LauncherFileName = "FeatherScribe.cmd";
    private const string AppProjectRelativePath = "src/FeatherScribe.App/FeatherScribe.App.csproj";

    private static readonly Regex AbsoluteDrivePath = new(@"[A-Za-z]:\\", RegexOptions.CultureInvariant);

    private static readonly Regex ExePath =
        new(@"src\\FeatherScribe\.App\\bin\\[^""\s]*?\.exe", RegexOptions.CultureInvariant);

    [Fact]
    public void Launcher_IsAsciiWithCrlfAndRelativePaths()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            return;
        }

        var path = Path.Combine(root, LauncherFileName);
        Assert.True(File.Exists(path), $"{LauncherFileName} not found at the repository root.");

        var bytes = File.ReadAllBytes(path);
        Assert.NotEmpty(bytes);
        Assert.All(bytes, value => Assert.True(value < 0x80, "FeatherScribe.cmd must be ASCII only."));

        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\n')
            {
                Assert.True(index > 0 && bytes[index - 1] == (byte)'\r', $"Bare LF at byte offset {index}.");
            }
        }

        Assert.Equal((byte)'\n', bytes[^1]);

        var text = File.ReadAllText(path);
        Assert.DoesNotMatch(AbsoluteDrivePath, text);
        Assert.Contains("%~dp0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Launcher_ExePathMatchesAppProjectOutput()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            return;
        }

        var project = XDocument.Load(Path.Combine(root, AppProjectRelativePath));
        var targetFramework = project.Descendants("TargetFramework").Select(element => element.Value.Trim()).FirstOrDefault();
        Assert.False(string.IsNullOrEmpty(targetFramework), "App csproj has no TargetFramework.");

        var assemblyName = project.Descendants("AssemblyName").Select(element => element.Value.Trim()).FirstOrDefault();
        if (string.IsNullOrEmpty(assemblyName))
        {
            assemblyName = Path.GetFileNameWithoutExtension(AppProjectRelativePath);
        }

        var expected = $@"src\FeatherScribe.App\bin\Release\{targetFramework}\{assemblyName}.exe";

        var text = File.ReadAllText(Path.Combine(root, LauncherFileName));
        var matches = ExePath.Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();

        Assert.Equal([expected], matches);
    }

    [Fact]
    public void Launcher_ContainsSingleInstanceCheckDryRunReleaseBuildAndStart()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            return;
        }

        var text = File.ReadAllText(Path.Combine(root, LauncherFileName));

        var tasklistLine = text.Split('\n').FirstOrDefault(line => line.Contains("tasklist", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tasklistLine);
        Assert.Contains("FeatherScribe.App.exe", tasklistLine, StringComparison.Ordinal);

        Assert.Contains("FEATHERSCRIBE_LAUNCH_DRY_RUN", text, StringComparison.Ordinal);
        Assert.Contains("-c Release", text, StringComparison.Ordinal);
        Assert.Contains("start \"\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GitAttributes_KeepsBatchFilesCrlf()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            return;
        }

        var lines = File.ReadAllLines(Path.Combine(root, ".gitattributes")).Select(line => line.Trim());
        Assert.Contains("*.cmd text eol=crlf", lines);
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
