using System.IO;
using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// prompts/ ディレクトリからモード別テンプレートを読み込む。
/// ファイルが無い場合は最低限の組み込みテンプレートで動作を続ける。
/// </summary>
public sealed class FilePromptProvider : IPromptProvider
{
    private const string FallbackTemplate =
        """
        あなたは日本語音声入力の後処理エンジンです。
        以下の文字起こし結果を、意味を変えずに自然で読みやすい日本語に整形してください。
        フィラー語を削除し、句読点を補ってください。
        出力は整形後テキストのみとし、解説や前置きは出さないでください。

        用語辞書:
        {{dictionary}}

        文字起こし:
        <<<
        {{raw_transcript}}
        >>>
        """;

    private static readonly Dictionary<FormattingMode, string> FileNames = new()
    {
        [FormattingMode.PlainFast] = "plain.md",
        [FormattingMode.PlainQuality] = "plain.md",
        [FormattingMode.Polite] = "polite.md",
        [FormattingMode.Bullet] = "bullet.md",
        [FormattingMode.Memo] = "memo.md",
        [FormattingMode.DevInstruction] = "dev_instruction.md",
    };

    private readonly string _promptsDirectory;

    public FilePromptProvider(string rootPath)
    {
        _promptsDirectory = Path.Combine(rootPath, "prompts");
    }

    public string GetTemplate(FormattingMode mode)
    {
        if (mode == FormattingMode.NoFormat)
        {
            return PromptBuilder.TranscriptPlaceholder;
        }

        if (!FileNames.TryGetValue(mode, out var fileName))
        {
            return FallbackTemplate;
        }

        var path = Path.Combine(_promptsDirectory, fileName);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : FallbackTemplate;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FallbackTemplate;
        }
    }

    public string GetPromptFileName(FormattingMode mode)
        => mode == FormattingMode.NoFormat
            ? "(none)"
            : FileNames.TryGetValue(mode, out var fileName)
                ? fileName
                : "(fallback)";
}
