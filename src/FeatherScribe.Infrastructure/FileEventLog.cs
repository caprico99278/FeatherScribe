using System.IO;
using System.Text.Json;
using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// メタデータのみを logs/events.log へ JSON Lines 形式で追記する(指示書§11.2)。
/// 本文・音声データは書き込まない。ログ失敗でアプリを落とさない。
/// </summary>
public sealed class FileEventLog : IEventLog
{
    private readonly string _logPath;
    private readonly Lock _gate = new();

    public FileEventLog(string rootPath)
    {
        _logPath = Path.Combine(rootPath, "logs", "events.log");
    }

    public void Write(PipelineEvent entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(entry);
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                File.AppendAllText(_logPath, line + "\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ログはベストエフォート
        }
    }
}
