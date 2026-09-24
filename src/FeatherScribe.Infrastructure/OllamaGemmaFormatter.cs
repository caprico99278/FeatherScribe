using System.Net.Http;
using System.Text;
using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// Ollama API 経由で Gemma 4 による整形を行う。
/// 失敗・検証不合格の場合は raw text をそのまま返し UsedFallback=true とする(指示書§7.4, §12.2)。
/// </summary>
public sealed class OllamaGemmaFormatter : ITextFormatter
{
    private readonly HttpClient _httpClient;
    private readonly LlmSettings _settings;
    private readonly IPromptProvider _promptProvider;
    private readonly IEventLog? _eventLog;

    public OllamaGemmaFormatter(
        HttpClient httpClient,
        LlmSettings settings,
        IPromptProvider promptProvider,
        IEventLog? eventLog = null)
    {
        _httpClient = httpClient;
        _settings = settings;
        _promptProvider = promptProvider;
        _eventLog = eventLog;
    }

    public async Task<FormatResult> FormatAsync(
        FormatRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Mode == FormattingMode.NoFormat)
        {
            return new FormatResult(request.RawText, UsedFallback: false, ErrorMessage: null);
        }

        // PlainQualityのみ高品質モデル+長タイムアウト。他モードは軽量モデル+短タイムアウト。
        var isQuality = request.Mode == FormattingMode.PlainQuality;
        var model = isQuality ? _settings.QualityModel : _settings.Model;
        var timeoutSeconds = isQuality ? _settings.QualityTimeoutSeconds : _settings.TimeoutSeconds;

        try
        {
            var promptFileName = _promptProvider is FilePromptProvider filePromptProvider
                ? filePromptProvider.GetPromptFileName(request.Mode)
                : "(unknown)";
            _eventLog?.Write(new PipelineEvent(
                DateTimeOffset.Now,
                "format_profile_prompt",
                true,
                $"profile={request.Mode};prompt={promptFileName}",
                0,
                request.Mode.ToString(),
                model,
                request.RawText.Length));

            var template = _promptProvider.GetTemplate(request.Mode);
            var prompt = PromptBuilder.Build(template, request.DictionaryEntries, request.RawText);
            var requestJson = OllamaRequestBuilder.BuildChatRequestJson(
                model,
                prompt,
                _settings.Temperature,
                _settings.GpuLayers,
                _settings.NumPredict,
                _settings.NumContext,
                _settings.KeepAlive);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await _httpClient
                .PostAsync(_settings.Endpoint.TrimEnd('/') + OllamaRequestBuilder.ChatPath, content, timeoutCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Fallback(request, $"LLM API エラー: HTTP {(int)response.StatusCode}");
            }

            var responseJson = await response.Content
                .ReadAsStringAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            var parsed = OllamaRequestBuilder.ParseChatResponse(responseJson);
            var formatted = FormatPostProcessor.RemoveThinkTags(parsed).Text;

            var validation = FormatResultValidator.Validate(request.RawText, formatted);
            if (!validation.IsValid)
            {
                if (request.Mode is FormattingMode.PlainFast or FormattingMode.PlainQuality)
                {
                    var conservative = ConservativePlainFormatter.Format(request.RawText);
                    var conservativeValidation = FormatResultValidator.Validate(request.RawText, conservative);
                    if (conservativeValidation.IsValid)
                    {
                        return new FormatResult(conservative, UsedFallback: false, ErrorMessage: null, RejectedText: formatted?.Trim());
                    }
                }

                return Fallback(request, $"整形結果を破棄しました: {validation.Reason}", formatted?.Trim());
            }

            return new FormatResult(formatted!.Trim(), UsedFallback: false, ErrorMessage: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Fallback(request, $"LLM がタイムアウトしました ({timeoutSeconds}秒)");
        }
        catch (HttpRequestException ex)
        {
            return Fallback(request, $"LLM へ接続できません: {ex.Message}");
        }
    }

    private static FormatResult Fallback(FormatRequest request, string reason, string? rejectedText = null)
        => new(request.RawText, UsedFallback: true, ErrorMessage: reason, RejectedText: rejectedText);
}
