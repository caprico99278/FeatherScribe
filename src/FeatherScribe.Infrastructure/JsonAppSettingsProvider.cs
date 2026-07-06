using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public string LocalSettingsPath => Path.Combine(RootPath, "config", "appsettings.local.json");

    public string? LastError { get; private set; }
    public IReadOnlyList<string> LoadedSettingsPaths => _loadedSettingsPaths;
    public IReadOnlyList<string> LastWarnings => _lastWarnings;

    private readonly List<string> _loadedSettingsPaths = [];
    private readonly List<string> _lastWarnings = [];

    public JsonAppSettingsProvider(string rootPath)
    {
        RootPath = rootPath;
    }

    public AppSettings Load()
    {
        _loadedSettingsPaths.Clear();
        _lastWarnings.Clear();
        LastError = null;

        var settingsNode = LoadBaseSettingsNode();
        MergeLocalSettingsNode(settingsNode);

        var settings = settingsNode.Deserialize<AppSettings>(SerializerOptions) ?? new AppSettings();

        return ResolvePaths(settings);
    }

    private JsonObject LoadBaseSettingsNode()
    {
        try
        {
            var json = File.ReadAllText(SettingsPath);
            var node = JsonNode.Parse(json)?.AsObject();
            _loadedSettingsPaths.Add(SettingsPath);
            return node ?? DefaultSettingsNode();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            _lastWarnings.Add($"Failed to load {SettingsPath}: {LastError}");
            return DefaultSettingsNode();
        }
    }

    private void MergeLocalSettingsNode(JsonObject settingsNode)
    {
        if (!File.Exists(LocalSettingsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(LocalSettingsPath);
            var localNode = JsonNode.Parse(json)?.AsObject();
            if (localNode is null)
            {
                _lastWarnings.Add($"Ignored {LocalSettingsPath}: root JSON value is not an object.");
                return;
            }

            MergeObjects(settingsNode, localNode);
            _loadedSettingsPaths.Add(LocalSettingsPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            var warning = $"{ex.GetType().Name}: {ex.Message}";
            _lastWarnings.Add($"Ignored {LocalSettingsPath}: {warning}");
        }
    }

    private static JsonObject DefaultSettingsNode()
        => JsonSerializer.SerializeToNode(new AppSettings(), SerializerOptions)?.AsObject() ?? [];

    private static void MergeObjects(JsonObject target, JsonObject overlay)
    {
        foreach (var property in overlay)
        {
            if (property.Value is JsonObject overlayObject &&
                target[property.Key] is JsonObject targetObject)
            {
                MergeObjects(targetObject, overlayObject);
                continue;
            }

            target[property.Key] = property.Value?.DeepClone();
        }
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
