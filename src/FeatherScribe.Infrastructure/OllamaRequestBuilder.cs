using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// Ollama /api/chat のリクエスト生成とレスポンス解析。
/// </summary>
public static class OllamaRequestBuilder
{
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
        int? gpuLayers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new ChatRequest(
            model,
            [new ChatMessage("user", prompt)],
            Stream: false,
            new ChatOptions(temperature, gpuLayers));

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
        ChatOptions Options);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatOptions(
        double Temperature,
        [property: JsonPropertyName("num_gpu")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? NumGpu);
}
