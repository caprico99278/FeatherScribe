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

    // The repo-local ollama must store models in local/ollama-models (the folder
    // tools/start_ollama_server.ps1 serves from), and an existing model must not be pulled again.
    [Fact]
    public void SetupGemmaOllama_UsesRepoModelFolderAndSkipsExistingModel()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "setup_gemma_ollama.ps1"));

        Assert.Contains("\"ollama-models\"", script);
        var setModels = script.IndexOf("$env:OLLAMA_MODELS =", StringComparison.Ordinal);
        var startServer = script.IndexOf("Start-Process -FilePath $ollamaExe", StringComparison.Ordinal);
        var queryTags = script.IndexOf("/api/tags", StringComparison.Ordinal);
        var pull = script.IndexOf("& $ollamaExe pull", StringComparison.Ordinal);

        Assert.True(setModels >= 0, "OLLAMA_MODELS is not set.");
        Assert.True(startServer > setModels, "OLLAMA_MODELS must be set before the server is started.");
        Assert.True(queryTags >= 0 && pull > queryTags, "/api/tags must be queried before pulling.");
        Assert.Contains("Model already exists. Skipping pull.", script);
    }

    // Thinking models (gemma4) spend the token budget on hidden reasoning unless think is
    // disabled, so the manual format check must send think=false like the app does.
    [Fact]
    public void RunFormatTest_DisablesThinking()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "run_format_test.ps1"));

        Assert.Matches(@"(?m)^\s*think\s*=\s*\$false\b", script);
    }

    // Generated sample outputs (samples/raw, samples/formatted, samples/audio) can contain real
    // transcripts, so the archive must mirror the .gitignore allow-list instead of packing them.
    [Fact]
    public void ArchiveScript_AllowListsOnlyVerifiedSampleFiles()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "archive_featherscribe.ps1"));

        Assert.Contains("\"samples/raw/sample_001_raw.txt\"", script);
        Assert.Contains("\"samples/formatted/sample_001_formatted.txt\"", script);
        Assert.Contains("$allowListedSampleFiles -contains $relativeName", script);
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
