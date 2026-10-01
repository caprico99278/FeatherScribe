using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// Detects VSCode's "copied from an empty selection" marker. With no selection, VSCode copies the whole
/// current line (editor.emptySelectionClipboard, on by default) and attaches the metadata
/// <c>vscode-editor-data</c> = <c>{"isFromEmptySelection":true,...}</c>. Pasting an edit of that line would
/// insert a duplicate line instead of replacing a selection, so selected-text editing treats it as "no selection".
/// The metadata is looked up as its own clipboard format and, because VSCode (Chromium) writes web custom
/// types into one pickled map, also inside the Chromium custom data format. Pure parsing, no clipboard access;
/// anything unreadable or malformed means "not marked".
/// </summary>
internal static class VsCodeClipboardMarker
{
    public const string VsCodeEditorDataFormat = "vscode-editor-data";
    public const string ChromiumCustomDataFormat = "Chromium Web Custom MIME Data Format";

    private const int MaxCustomDataEntries = 4096;

    /// <summary>True when any marker source says the copy came from an empty selection.</summary>
    /// <param name="formats">Formats present on the copied data object.</param>
    /// <param name="getData">Reads one format (may throw; treated as "not marked").</param>
    public static bool IsFromEmptySelection(IReadOnlyCollection<string> formats, Func<string, object?> getData)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(getData);

        if (formats.Contains(VsCodeEditorDataFormat, StringComparer.Ordinal) &&
            IsEmptySelectionJson(DecodeText(TryGetData(getData, VsCodeEditorDataFormat))))
        {
            return true;
        }

        return formats.Contains(ChromiumCustomDataFormat, StringComparer.Ordinal) &&
            ToBytes(TryGetData(getData, ChromiumCustomDataFormat)) is { } pickle &&
            IsEmptySelectionJson(TryReadChromiumCustomData(pickle, VsCodeEditorDataFormat));
    }

    /// <summary>True only for a JSON object whose <c>isFromEmptySelection</c> is the literal true.</summary>
    public static bool IsEmptySelectionJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json.Trim().TrimEnd('\0'));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("isFromEmptySelection", out var value) &&
                value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads one entry of Chromium's web custom data pickle: uint32 payload size, uint32 entry count, then
    /// per entry a UTF-16 type and a UTF-16 value, each written as int32 length (chars) plus the characters
    /// padded to 4 bytes. Returns null when the type is absent or the data is malformed.
    /// </summary>
    public static string? TryReadChromiumCustomData(ReadOnlySpan<byte> pickle, string type)
    {
        if (pickle.Length < 8)
        {
            return null;
        }

        var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(pickle);
        if (payloadSize > (uint)(pickle.Length - 4))
        {
            return null;
        }

        var end = 4 + (int)payloadSize;
        var offset = 4;
        if (!TryReadUInt32(pickle, end, ref offset, out var count) || count > MaxCustomDataEntries)
        {
            return null;
        }

        for (var i = 0; i < count; i++)
        {
            if (!TryReadString16(pickle, end, ref offset, out var key) ||
                !TryReadString16(pickle, end, ref offset, out var value))
            {
                return null;
            }

            if (string.Equals(key, type, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Clipboard data of a custom format as text: a string as is, bytes as UTF-16LE or UTF-8.</summary>
    internal static string? DecodeText(object? data)
    {
        if (data is string text)
        {
            return text;
        }

        if (ToBytes(data) is not { Length: > 0 } bytes)
        {
            return null;
        }

        var looksUtf16 = bytes.Length >= 2 && bytes.Length % 2 == 0 && bytes[1] == 0;
        return (looksUtf16 ? Encoding.Unicode : Encoding.UTF8).GetString(bytes).TrimEnd('\0');
    }

    private static byte[]? ToBytes(object? data) => data switch
    {
        byte[] bytes => bytes,
        MemoryStream stream => stream.ToArray(),
        _ => null,
    };

    private static object? TryGetData(Func<string, object?> getData, string format)
    {
        try
        {
            return getData(format);
        }
        catch (Exception)
        {
            // An unreadable marker is no marker: the selection is then handled as a normal selection.
            return null;
        }
    }

    private static bool TryReadUInt32(ReadOnlySpan<byte> pickle, int end, ref int offset, out uint value)
    {
        value = 0;
        if (offset > end - 4)
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(pickle[offset..]);
        offset += 4;
        return true;
    }

    private static bool TryReadString16(ReadOnlySpan<byte> pickle, int end, ref int offset, out string value)
    {
        value = "";
        if (!TryReadUInt32(pickle, end, ref offset, out var length) || length > (uint)(end - offset) / 2)
        {
            return false;
        }

        var byteCount = (int)length * 2;
        value = Encoding.Unicode.GetString(pickle.Slice(offset, byteCount));
        // Padding to 4 bytes; a missing final pad only makes the next read fail its bounds check.
        offset += (byteCount + 3) & ~3;
        return true;
    }
}
