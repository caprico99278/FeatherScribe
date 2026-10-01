using System.Buffers.Binary;
using System.Text;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

/// <summary>VSCode empty-selection marker parsing (pure; no clipboard access).</summary>
public sealed class VsCodeClipboardMarkerTests
{
    private const string MarkedJson = """{"version":1,"isFromEmptySelection":true,"multicursorText":null,"mode":"plaintext"}""";
    private const string UnmarkedJson = """{"version":1,"isFromEmptySelection":false,"multicursorText":null,"mode":"plaintext"}""";

    [Theory]
    [InlineData(MarkedJson, true)]
    [InlineData(UnmarkedJson, false)]
    [InlineData("""{"version":1,"mode":"plaintext"}""", false)] // missing
    [InlineData("""{"isFromEmptySelection":"true"}""", false)] // not the literal true
    [InlineData("""{"isFromEmptySelection":tru""", false)] // malformed
    [InlineData("not json", false)]
    [InlineData("[true]", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEmptySelectionJson(string? json, bool expected)
    {
        Assert.Equal(expected, VsCodeClipboardMarker.IsEmptySelectionJson(json));
    }

    [Fact]
    public void IsEmptySelectionJson_ToleratesTrailingNulls()
    {
        Assert.True(VsCodeClipboardMarker.IsEmptySelectionJson(MarkedJson + "\0\0"));
    }

    [Theory]
    [InlineData(MarkedJson, true)]
    [InlineData(UnmarkedJson, false)]
    [InlineData("{broken", false)]
    public void OwnFormat_AsStringUtf8OrUtf16(string json, bool expected)
    {
        string[] formats = ["UnicodeText", VsCodeClipboardMarker.VsCodeEditorDataFormat];

        Assert.Equal(expected, VsCodeClipboardMarker.IsFromEmptySelection(formats, _ => json));
        Assert.Equal(expected, VsCodeClipboardMarker.IsFromEmptySelection(
            formats, _ => new MemoryStream(Encoding.UTF8.GetBytes(json + "\0"))));
        Assert.Equal(expected, VsCodeClipboardMarker.IsFromEmptySelection(
            formats, _ => new MemoryStream(Encoding.Unicode.GetBytes(json + "\0"))));
    }

    [Theory]
    [InlineData(MarkedJson, true)]
    [InlineData(UnmarkedJson, false)]
    [InlineData("{broken", false)]
    public void ChromiumCustomData_ReadsVsCodeEntry(string json, bool expected)
    {
        var pickle = Pickle(("text/plain", "line"), (VsCodeClipboardMarker.VsCodeEditorDataFormat, json));
        string[] formats = ["UnicodeText", VsCodeClipboardMarker.ChromiumCustomDataFormat];

        Assert.Equal(expected, VsCodeClipboardMarker.IsFromEmptySelection(formats, _ => new MemoryStream(pickle)));
    }

    [Fact]
    public void ChromiumCustomData_WithoutVsCodeEntry_IsNotMarked()
    {
        var pickle = Pickle(("text/x-other", MarkedJson));

        Assert.Null(VsCodeClipboardMarker.TryReadChromiumCustomData(pickle, VsCodeClipboardMarker.VsCodeEditorDataFormat));
    }

    [Fact]
    public void ChromiumCustomData_OddLengthStringsArePadded()
    {
        var pickle = Pickle(("abc", "x"), (VsCodeClipboardMarker.VsCodeEditorDataFormat, MarkedJson));

        Assert.Equal(MarkedJson, VsCodeClipboardMarker.TryReadChromiumCustomData(pickle, VsCodeClipboardMarker.VsCodeEditorDataFormat));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(30)]
    public void ChromiumCustomData_Truncated_IsNull(int length)
    {
        var pickle = Pickle((VsCodeClipboardMarker.VsCodeEditorDataFormat, MarkedJson));

        Assert.Null(VsCodeClipboardMarker.TryReadChromiumCustomData(pickle.AsSpan(0, length), VsCodeClipboardMarker.VsCodeEditorDataFormat));
    }

    [Fact]
    public void ChromiumCustomData_HugeLengthOrCount_IsNull()
    {
        var pickle = Pickle((VsCodeClipboardMarker.VsCodeEditorDataFormat, MarkedJson));
        var hugeLength = (byte[])pickle.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(hugeLength.AsSpan(8), int.MaxValue);
        var negativeLength = (byte[])pickle.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(negativeLength.AsSpan(8), -1);
        var hugeCount = (byte[])pickle.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(hugeCount.AsSpan(4), uint.MaxValue);

        Assert.Null(VsCodeClipboardMarker.TryReadChromiumCustomData(hugeLength, VsCodeClipboardMarker.VsCodeEditorDataFormat));
        Assert.Null(VsCodeClipboardMarker.TryReadChromiumCustomData(negativeLength, VsCodeClipboardMarker.VsCodeEditorDataFormat));
        Assert.Null(VsCodeClipboardMarker.TryReadChromiumCustomData(hugeCount, VsCodeClipboardMarker.VsCodeEditorDataFormat));
    }

    [Fact]
    public void NoMarkerFormat_IsNotMarked_AndDataIsNotRead()
    {
        var reads = 0;

        var marked = VsCodeClipboardMarker.IsFromEmptySelection(["UnicodeText", "Text"], _ =>
        {
            reads++;
            return MarkedJson;
        });

        Assert.False(marked);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void UnreadableMarker_IsNotMarked()
    {
        string[] formats = [VsCodeClipboardMarker.VsCodeEditorDataFormat, VsCodeClipboardMarker.ChromiumCustomDataFormat];

        Assert.False(VsCodeClipboardMarker.IsFromEmptySelection(
            formats, _ => throw new System.Runtime.InteropServices.COMException("locked")));
        Assert.False(VsCodeClipboardMarker.IsFromEmptySelection(formats, _ => null));
        Assert.False(VsCodeClipboardMarker.IsFromEmptySelection(formats, _ => 42));
    }

    /// <summary>Chromium web custom data pickle: payload size, count, then (type, value) UTF-16 strings padded to 4 bytes.</summary>
    private static byte[] Pickle(params (string Type, string Value)[] entries)
    {
        var payload = new List<byte>();
        AppendUInt32(payload, (uint)entries.Length);
        foreach (var (type, value) in entries)
        {
            AppendString16(payload, type);
            AppendString16(payload, value);
        }

        var result = new List<byte>();
        AppendUInt32(result, (uint)payload.Count);
        result.AddRange(payload);
        return [.. result];
    }

    private static void AppendUInt32(List<byte> target, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        target.AddRange(bytes);
    }

    private static void AppendString16(List<byte> target, string value)
    {
        AppendUInt32(target, (uint)value.Length);
        var bytes = Encoding.Unicode.GetBytes(value);
        target.AddRange(bytes);
        while (target.Count % 4 != 0)
        {
            target.Add(0);
        }
    }
}
