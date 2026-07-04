using System.Text.Json;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

public class OllamaRequestBuilderTests
{
    [Fact]
    public void BuildChatRequestJson_ProducesExpectedShape()
    {
        var json = OllamaRequestBuilder.BuildChatRequestJson("gemma4:e4b", "整形して", 0.1);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("gemma4:e4b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(0.1, root.GetProperty("options").GetProperty("temperature").GetDouble());

        var message = root.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("整形して", message.GetProperty("content").GetString());
    }

    [Fact]
    public void BuildChatRequestJson_JapaneseIsNotEscapedBeyondJson()
    {
        var json = OllamaRequestBuilder.BuildChatRequestJson("m", "こんにちは「テスト」", 0);

        Assert.Contains("こんにちは「テスト」", json);
    }

    [Fact]
    public void BuildChatRequestJson_DefaultGpuLayers_OmitsNumGpu()
    {
        var json = OllamaRequestBuilder.BuildChatRequestJson("m", "p", 0.1);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("options").TryGetProperty("num_gpu", out _));
    }

    [Fact]
    public void BuildChatRequestJson_ZeroGpuLayers_IncludesNumGpu()
    {
        var json = OllamaRequestBuilder.BuildChatRequestJson("m", "p", 0.1, gpuLayers: 0);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(0, document.RootElement.GetProperty("options").GetProperty("num_gpu").GetInt32());
    }

    [Fact]
    public void BuildChatRequestJson_EmptyModel_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => OllamaRequestBuilder.BuildChatRequestJson("", "prompt", 0.1));
    }

    [Fact]
    public void ParseChatResponse_ExtractsMessageContent()
    {
        const string response =
            """{"model":"gemma4:e4b","message":{"role":"assistant","content":"整形済みテキスト"},"done":true}""";

        Assert.Equal("整形済みテキスト", OllamaRequestBuilder.ParseChatResponse(response));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"message":{}}""")]
    [InlineData("""{"message":{"content":123}}""")]
    public void ParseChatResponse_InvalidShape_ReturnsNull(string response)
    {
        Assert.Null(OllamaRequestBuilder.ParseChatResponse(response));
    }
}
