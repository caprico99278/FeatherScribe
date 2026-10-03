using System.Text.Json;
using System.Text.Json.Nodes;

namespace FeatherScribe.Launcher;

/// <summary>An installed Ollama model as listed by <c>GET /api/tags</c>.</summary>
internal sealed record OllamaModel(string Name, long SizeBytes);

/// <summary>Parses the <c>GET /api/tags</c> response.</summary>
internal static class OllamaTags
{
    /// <summary>Installed models sorted by name; malformed JSON → empty, entries without a name are skipped.</summary>
    public static IReadOnlyList<OllamaModel> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("models", out var models) ||
                models.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<OllamaModel>();
            foreach (var model in models.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.Object ||
                    !model.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(name.GetString()))
                {
                    continue;
                }

                var size = model.TryGetProperty("size", out var sizeElement) &&
                    sizeElement.ValueKind == JsonValueKind.Number &&
                    sizeElement.TryGetInt64(out var bytes)
                    ? bytes
                    : 0;
                result.Add(new OllamaModel(name.GetString()!.Trim(), size));
            }

            return [.. result.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>Pure request/response helpers of the Ollama HTTP API used by the launcher.</summary>
internal static class OllamaApi
{
    public const string VersionPath = "api/version";
    public const string TagsPath = "api/tags";
    public const string GeneratePath = "api/generate";

    /// <summary>
    /// <c>POST /api/generate</c> body without a prompt: Ollama only loads the model and keeps it for
    /// <paramref name="keepAlive"/>. num_gpu / num_ctx are sent like the app's format requests
    /// (llm.gpuLayers / llm.numContext), so the model is loaded the way the app uses it (e.g. CPU only)
    /// and the first format does not reload it.
    /// </summary>
    public static string BuildLoadRequestJson(string model, string keepAlive, int? gpuLayers, int? numContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(keepAlive);

        var request = new JsonObject
        {
            ["model"] = model,
            ["keep_alive"] = keepAlive,
        };

        var options = new JsonObject();
        if (gpuLayers is { } numGpu)
        {
            options["num_gpu"] = numGpu;
        }

        if (numContext is { } numCtx)
        {
            options["num_ctx"] = numCtx;
        }

        if (options.Count > 0)
        {
            request["options"] = options;
        }

        return request.ToJsonString();
    }

    /// <summary>The "error" text of an Ollama error response, or null.</summary>
    public static string? ParseError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
