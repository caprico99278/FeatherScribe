using FeatherScribe.Tools.AsrBenchmark;

namespace FeatherScribe.Tests;

public class AudioSetMarkerTests : IDisposable
{
    private static readonly DateTimeOffset Earlier = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset Later = new(2026, 10, 1, 19, 0, 0, TimeSpan.FromHours(9));

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "FeatherScribeTests", "audioset_" + Guid.NewGuid().ToString("N"));

    public AudioSetMarkerTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string MarkerPath => Path.Combine(_directory, AudioSetMarker.FileName);

    [Fact]
    public void PrepareForRecording_EmptyFolder_WritesRealMarker()
    {
        var conflict = AudioSetMarker.PrepareForRecording(
            _directory, AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Earlier);

        Assert.Null(conflict);
        var marker = AudioSetMarker.Read(_directory);
        Assert.Equal(AudioSetKind.Real, marker.Kind);
        Assert.Equal(AudioSetMarker.RealRecordingSource, marker.Source);
        Assert.Equal(Earlier, marker.CreatedAt);
    }

    [Fact]
    public void PrepareForRecording_CreatesMissingFolder()
    {
        var nested = Path.Combine(_directory, "audio");

        Assert.Null(AudioSetMarker.PrepareForRecording(nested, AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Earlier));
        Assert.Equal(AudioSetKind.Real, AudioSetMarker.Read(nested).Kind);
    }

    [Fact]
    public void PrepareForRecording_ExistingRealMarker_IsRefreshed()
    {
        AudioSetMarker.Write(_directory, new AudioSetInfo(AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Earlier));

        var conflict = AudioSetMarker.PrepareForRecording(
            _directory, AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Later);

        Assert.Null(conflict);
        var marker = AudioSetMarker.Read(_directory);
        Assert.Equal(AudioSetKind.Real, marker.Kind);
        Assert.Equal(Later, marker.CreatedAt);
    }

    [Fact]
    public void PrepareForRecording_SyntheticFolder_IsAConflictForRecord()
    {
        AudioSetMarker.Write(_directory, new AudioSetInfo(AudioSetKind.Synthetic, "System.Speech TTS", Earlier));

        var conflict = AudioSetMarker.PrepareForRecording(
            _directory, AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Later);

        Assert.Equal(AudioSetMarker.RecordIntoSyntheticMessage, conflict);
        var marker = AudioSetMarker.Read(_directory);
        Assert.Equal(AudioSetKind.Synthetic, marker.Kind);
        Assert.Equal(Earlier, marker.CreatedAt);
    }

    [Fact]
    public void PrepareForRecording_RealFolder_IsAConflictForSynthesize()
    {
        AudioSetMarker.Write(_directory, new AudioSetInfo(AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Earlier));

        var conflict = AudioSetMarker.PrepareForRecording(_directory, AudioSetKind.Synthetic, "System.Speech TTS", Later);

        Assert.Equal(AudioSetMarker.SynthesizeIntoRealMessage, conflict);
        Assert.Equal(AudioSetKind.Real, AudioSetMarker.Read(_directory).Kind);
    }

    [Fact]
    public void PrepareForRecording_ExistingSyntheticMarker_IsRefreshedForSynthesize()
    {
        AudioSetMarker.Write(_directory, new AudioSetInfo(AudioSetKind.Synthetic, "System.Speech TTS", Earlier));

        Assert.Null(AudioSetMarker.PrepareForRecording(_directory, AudioSetKind.Synthetic, "System.Speech TTS", Later));
        Assert.Equal(Later, AudioSetMarker.Read(_directory).CreatedAt);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"kind":"mystery","source":null,"createdAt":"2026-10-01T09:00:00+09:00"}""")]
    [InlineData("null")]
    public void PrepareForRecording_UnknownOrMalformedMarker_IsTreatedAsAbsentAndOverwritten(string content)
    {
        File.WriteAllText(MarkerPath, content);
        Assert.Equal(AudioSetKind.Unknown, AudioSetMarker.Read(_directory).Kind);

        var conflict = AudioSetMarker.PrepareForRecording(
            _directory, AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Later);

        Assert.Null(conflict);
        Assert.Equal(AudioSetKind.Real, AudioSetMarker.Read(_directory).Kind);
    }

    [Fact]
    public void TryRefresh_UpdatesTimestamp()
    {
        AudioSetMarker.Write(_directory, new AudioSetInfo(AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Earlier));

        Assert.Null(AudioSetMarker.TryRefresh(_directory, AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Later));
        Assert.Equal(Later, AudioSetMarker.Read(_directory).CreatedAt);
    }

    [Fact]
    public void TryRefresh_WriteFailure_ReturnsMessageInsteadOfThrowing()
    {
        var missingFolder = Path.Combine(_directory, "does-not-exist");

        var error = AudioSetMarker.TryRefresh(missingFolder, AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Later);

        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Write_ProducesLfJsonWithoutBom()
    {
        AudioSetMarker.Write(_directory, new AudioSetInfo(AudioSetKind.Real, AudioSetMarker.RealRecordingSource, Earlier));

        var bytes = File.ReadAllBytes(MarkerPath);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
    }
}
