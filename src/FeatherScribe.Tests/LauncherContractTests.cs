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
    private const string LauncherProjectRelativePath = "src/FeatherScribe.Launcher/FeatherScribe.Launcher.csproj";

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
    public void Launcher_ContainsSingleInstanceCheckDryRunAndReleaseBuilds()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            return;
        }

        var text = File.ReadAllText(Path.Combine(root, LauncherFileName));
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

        var tasklistIndex = Array.FindIndex(lines, line => line.Contains("tasklist", StringComparison.OrdinalIgnoreCase));
        Assert.True(tasklistIndex >= 0, "Single-instance check (tasklist) is missing.");
        Assert.Contains("FeatherScribe.App.exe", lines[tasklistIndex], StringComparison.Ordinal);

        Assert.Contains("FEATHERSCRIBE_LAUNCH_DRY_RUN", text, StringComparison.Ordinal);

        // Both projects are built explicitly in Release (the launcher must not reference the WPF App).
        var appBuildIndex = Array.FindIndex(lines, line =>
            line.StartsWith("dotnet build \"%FS_PROJECT%\" -c Release", StringComparison.Ordinal));
        var launcherBuildIndex = Array.FindIndex(lines, line =>
            line.StartsWith("dotnet build \"%FS_LAUNCHER_PROJECT%\" -c Release", StringComparison.Ordinal));
        Assert.True(appBuildIndex >= 0, "App Release build line is missing.");
        Assert.True(launcherBuildIndex >= 0, "Launcher Release build line is missing.");
        Assert.Contains($"set \"FS_PROJECT={AppProjectRelativePath.Replace('/', '\\')}\"", lines);
        Assert.Contains($"set \"FS_LAUNCHER_PROJECT={LauncherProjectRelativePath.Replace('/', '\\')}\"", lines);

        // The single-instance check runs before anything is built.
        Assert.True(tasklistIndex < appBuildIndex && tasklistIndex < launcherBuildIndex);

        // The launcher runs in this console (no start), gets the App exe and passes every argument through.
        var invocationIndex = Array.FindIndex(lines, line =>
            line.StartsWith("\"%FS_LAUNCHER_EXE%\"", StringComparison.Ordinal));
        Assert.True(invocationIndex > launcherBuildIndex, "Launcher invocation is missing or before the build.");
        Assert.Equal("\"%FS_LAUNCHER_EXE%\" --app \"%FS_EXE%\" %*", lines[invocationIndex]);
        Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("start ", StringComparison.OrdinalIgnoreCase));

        // Exit with the launcher's exit code; pause on non-zero so the message can be read.
        Assert.Equal("set \"FS_EXIT=%ERRORLEVEL%\"", lines[invocationIndex + 1]);
        Assert.Contains("if not \"%FS_EXIT%\"==\"0\" pause", lines);
        Assert.Contains("exit /b %FS_EXIT%", lines);
    }

    [Fact]
    public void Launcher_ConsoleLauncherPathMatchesLauncherProjectOutput()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            return;
        }

        var project = XDocument.Load(Path.Combine(root, LauncherProjectRelativePath));
        var targetFramework = project.Descendants("TargetFramework").Select(element => element.Value.Trim()).FirstOrDefault();
        Assert.False(string.IsNullOrEmpty(targetFramework), "Launcher csproj has no TargetFramework.");
        var assemblyName = project.Descendants("AssemblyName").Select(element => element.Value.Trim()).FirstOrDefault();
        Assert.Equal("FeatherScribe.Launcher", assemblyName);
        Assert.Equal("Exe", project.Descendants("OutputType").Select(element => element.Value.Trim()).FirstOrDefault());

        // The console launcher must not reference the WPF App project.
        Assert.DoesNotContain(
            project.Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include") ?? ""),
            include => include.Contains("FeatherScribe.App", StringComparison.OrdinalIgnoreCase));

        var lines = File.ReadAllText(Path.Combine(root, LauncherFileName)).Split('\n').Select(line => line.TrimEnd('\r'));
        Assert.Contains(
            $@"set ""FS_LAUNCHER_EXE=src\FeatherScribe.Launcher\bin\Release\{targetFramework}\{assemblyName}.exe""",
            lines);

        var solution = File.ReadAllText(Path.Combine(root, "FeatherScribe.slnx"));
        Assert.Contains($"<Project Path=\"{LauncherProjectRelativePath}\" />", solution, StringComparison.Ordinal);
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
