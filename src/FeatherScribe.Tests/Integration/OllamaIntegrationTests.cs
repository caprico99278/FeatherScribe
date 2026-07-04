using System.Net.Http;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests.Integration;

/// <summary>
/// Ollama API 疎通テスト。ローカルで Ollama が起動している場合のみ実行する。
/// 通常CIでは実行しない: dotnet test --filter "Category!=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class OllamaIntegrationTests
{
    private static readonly HttpClient HttpClient = new();

    private static async Task<bool> IsOllamaAvailableAsync(string endpoint)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await HttpClient.GetAsync($"{endpoint}/api/version", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Format_FillerText_ReturnsCleanedJapanese()
    {
        var root = AppRoot.Locate();
        var settings = new JsonAppSettingsProvider(root).Load();
        if (!await IsOllamaAvailableAsync(settings.Llm.Endpoint))
        {
            return; // Ollama 停止中のためスキップ
        }

        // 疎通確認用に十分なタイムアウトを取る (既定8秒はCPU実行では短すぎるため)
        var llm = new LlmSettings
        {
            Enabled = true,
            Endpoint = settings.Llm.Endpoint,
            Model = settings.Llm.Model,
            Temperature = settings.Llm.Temperature,
            TimeoutSeconds = 300,
            GpuLayers = settings.Llm.GpuLayers,
        };
        var formatter = new OllamaGemmaFormatter(HttpClient, llm, new FilePromptProvider(root));

        var result = await formatter.FormatAsync(
            new FormatRequest(
                "えーと、今日はですね、あの、天気がいいので、えー、散歩に行こうかなと思います",
                FormattingMode.PlainFast,
                []),
            CancellationToken.None);

        Assert.False(result.UsedFallback, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
    }

    [Fact]
    public async Task Format_UnreachableEndpoint_FallsBackToRawText()
    {
        const string raw = "えーと、テストです";
        var formatter = new OllamaGemmaFormatter(
            HttpClient,
            new LlmSettings { Endpoint = "http://localhost:59999", TimeoutSeconds = 3 },
            new FilePromptProvider(AppRoot.Locate()));

        var result = await formatter.FormatAsync(
            new FormatRequest(raw, FormattingMode.PlainFast, []),
            CancellationToken.None);

        Assert.True(result.UsedFallback);
        Assert.Equal(raw, result.Text);
        Assert.NotNull(result.ErrorMessage);
    }
}
