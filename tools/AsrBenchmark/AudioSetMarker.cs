using System.Text.Encodings.Web;
using System.Text.Json;

namespace FeatherScribe.Tools.AsrBenchmark;

/// <summary>Content of audioset.json, written next to the wavs by record / synthesize.</summary>
internal sealed record AudioSetInfo(string Kind, string? Source, DateTimeOffset CreatedAt);

internal static class AudioSetMarker
{
    public const string FileName = "audioset.json";

    /// <summary>Source recorded in the marker for the user's own recordings.</summary>
    public const string RealRecordingSource = "user recording (NAudio 16 kHz mono)";

    public const string RecordIntoSyntheticMessage =
        "This folder holds synthetic (TTS) audio. Record into a separate folder (default local/benchmark/audio).";

    public const string SynthesizeIntoRealMessage =
        "This folder holds real (user) recordings. Synthesize into a separate folder (default local/benchmark/tts).";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(string audioDirectory, AudioSetInfo info)
        => File.WriteAllText(
            Path.Combine(audioDirectory, FileName),
            JsonSerializer.Serialize(info, JsonOptions).ReplaceLineEndings("\n") + "\n");

    /// <summary>Reads the marker; a missing, unreadable or unrecognized marker means the kind is unknown.</summary>
    public static AudioSetInfo Read(string audioDirectory)
    {
        var path = Path.Combine(audioDirectory, FileName);
        if (!File.Exists(path))
        {
            return new AudioSetInfo(AudioSetKind.Unknown, null, DateTimeOffset.MinValue);
        }

        try
        {
            var info = JsonSerializer.Deserialize<AudioSetInfo>(File.ReadAllText(path), JsonOptions);
            return info is { Kind: AudioSetKind.Synthetic or AudioSetKind.Real }
                ? info
                : new AudioSetInfo(AudioSetKind.Unknown, info?.Source, info?.CreatedAt ?? DateTimeOffset.MinValue);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AudioSetInfo(AudioSetKind.Unknown, null, DateTimeOffset.MinValue);
        }
    }

    /// <summary>
    /// Called before the first wav is written into <paramref name="audioDirectory"/>. A folder already
    /// marked with the other kind (real vs synthetic) is a conflict: nothing is written and the message
    /// is returned. Otherwise the marker is written (or refreshed) as <paramref name="kind"/> and null is
    /// returned. A missing, malformed or unknown marker is treated as absent and overwritten.
    /// </summary>
    public static string? PrepareForRecording(string audioDirectory, string kind, string source, DateTimeOffset now)
    {
        if (kind is not (AudioSetKind.Real or AudioSetKind.Synthetic))
        {
            throw new ArgumentException($"Unsupported audio set kind: {kind}", nameof(kind));
        }

        var existing = Read(audioDirectory).Kind;
        if (kind == AudioSetKind.Real && existing == AudioSetKind.Synthetic)
        {
            return RecordIntoSyntheticMessage;
        }

        if (kind == AudioSetKind.Synthetic && existing == AudioSetKind.Real)
        {
            return SynthesizeIntoRealMessage;
        }

        Directory.CreateDirectory(audioDirectory);
        Write(audioDirectory, new AudioSetInfo(kind, source, now));
        return null;
    }

    /// <summary>
    /// Best-effort refresh of the marker timestamp after each saved utterance.
    /// Returns an error message on failure (the caller warns and continues), null on success.
    /// </summary>
    public static string? TryRefresh(string audioDirectory, string kind, string source, DateTimeOffset now)
    {
        try
        {
            Write(audioDirectory, new AudioSetInfo(kind, source, now));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
