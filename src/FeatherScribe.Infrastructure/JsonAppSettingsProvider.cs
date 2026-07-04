using System.IO;
using System.Text.Json;
using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// config/appsettings.json から設定を読み込む。壊れている場合は既定値で起動する。
/// asr の相対パスはルートディレクトリ基準で絶対パスへ解決する。
/// </summary>
public sealed class JsonAppSettingsProvider : IAppSettingsProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string RootPath { get; }
    public string SettingsPath => Path.Combine(RootPath, "config", "appsettings.json");

    public string? LastError { get; private set; }

    public JsonAppSettingsProvider(string rootPath)
    {
        RootPath = rootPath;
    }

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            var json = File.ReadAllText(SettingsPath);
            settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            settings = new AppSettings();
        }

        return ResolvePaths(settings);
    }

    private AppSettings ResolvePaths(AppSettings settings)
    {
        return new AppSettings
        {
            Asr = new AsrSettings
            {
                WhisperExecutablePath = ResolvePath(settings.Asr.WhisperExecutablePath),
                ModelPath = ResolvePath(settings.Asr.ModelPath),
                Language = settings.Asr.Language,
                Threads = Math.Max(1, settings.Asr.Threads),
                TimeoutSeconds = Math.Max(1, settings.Asr.TimeoutSeconds),
            },
            Llm = settings.Llm,
            Recording = settings.Recording,
            Output = settings.Output,
            Hotkeys = settings.Hotkeys,
            Privacy = settings.Privacy,
            Debug = settings.Debug,
        };
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(RootPath, path));
    }
}
