using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// Ollama /api/chat のリクエスト生成とレスポンス解析。
/// </summary>
public static class OllamaRequestBuilder
{
    private const string SystemPrompt =
        "Return only the final text. Do not add headings, lists, Markdown, labels, explanations, or content not present in the input.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public const string ChatPath = "/api/chat";

    public static string BuildChatRequestJson(
        string model,
        string prompt,
        double temperature,
        int? gpuLayers = null,
        int? numPredict = null,
        int? numContext = null,
        string? keepAlive = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new ChatRequest(
            model,
            [
                new ChatMessage("system", SystemPrompt),
                new ChatMessage("user", prompt),
            ],
            Stream: false,
            KeepAlive: keepAlive,
            new ChatOptions(temperature, gpuLayers, numPredict, numContext),
            Think: false);

        return JsonSerializer.Serialize(request, SerializerOptions);
    }

    /// <summary>レスポンス JSON から message.content を取り出す。形式不一致は null。</summary>
    public static string? ParseChatResponse(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            if (document.RootElement.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        bool Stream,
        [property: JsonPropertyName("keep_alive")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? KeepAlive,
        ChatOptions Options,
        // 整形に推論は不要なので常に think=false を送る。gemma4 などの思考対応モデルは未指定だと
        // 非表示の推論(message.thinking)にトークンを費やし、CPU 実測で 65 秒→7 秒の差が出る。
        // num_predict 指定時は推論だけで上限に達し content が空になることもある。
        bool Think);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatOptions(
        double Temperature,
        [property: JsonPropertyName("num_gpu")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? NumGpu,
        [property: JsonPropertyName("num_predict")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? NumPredict,
        [property: JsonPropertyName("num_ctx")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? NumContext);
}
