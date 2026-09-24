namespace FeatherScribe.Tests;

public class ToolScriptEncodingTests
{
    // Windows PowerShell 5.1 decodes BOM-less scripts with the ANSI code page (CP932 on
    // Japanese Windows). Non-ASCII bytes there can swallow line breaks and break parsing,
    // and the repository keeps scripts BOM-less, so tool scripts must stay ASCII-only.
    [Fact]
    public void ToolScripts_AreAsciiOnlyForWindowsPowerShell51()
    {
        var toolsDirectory = Path.Combine(FindRepoRoot(), "tools");
        var scripts = Directory.GetFiles(toolsDirectory, "*.ps1", SearchOption.AllDirectories);

        Assert.NotEmpty(scripts);
        foreach (var script in scripts)
        {
            var bytes = File.ReadAllBytes(script);
            var firstNonAscii = Array.FindIndex(bytes, b => b >= 0x80);
            Assert.True(
                firstNonAscii < 0,
                $"{Path.GetFileName(script)} contains a non-ASCII byte at offset {firstNonAscii}.");
        }
    }

    private static string FindRepoRoot()
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

        throw new DirectoryNotFoundException("Could not locate FeatherScribe repository root.");
    }
}
