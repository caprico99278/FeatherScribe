using System.IO;
using System.Text.Json;
using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// config/dictionary.json から個人辞書を読み込む。
/// ファイルが無い・壊れている場合は空辞書を返し、アプリを落とさない(指示書§8.3)。
/// </summary>
public sealed class JsonDictionaryProvider : IDictionaryProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _dictionaryPath;

    public JsonDictionaryProvider(string rootPath)
    {
        _dictionaryPath = Path.Combine(rootPath, "config", "dictionary.json");
    }

    public IReadOnlyList<DictionaryEntry> Load()
    {
        try
        {
            if (!File.Exists(_dictionaryPath))
            {
                return [];
            }

            var json = File.ReadAllText(_dictionaryPath);
            var document = JsonSerializer.Deserialize<DictionaryDocument>(json, SerializerOptions);

            return document?.Entries?
                .Where(e => !string.IsNullOrWhiteSpace(e.Canonical) && e.Patterns is { Count: > 0 })
                .Select(e => new DictionaryEntry(
                    e.Patterns!.Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
                    e.Canonical!))
                .Where(e => e.Patterns.Count > 0)
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private sealed class DictionaryDocument
    {
        public List<DictionaryEntryDto>? Entries { get; set; }
    }

    private sealed class DictionaryEntryDto
    {
        public List<string>? Patterns { get; set; }
        public string? Canonical { get; set; }
    }
}
