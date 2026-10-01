using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FeatherScribe.Tests;

/// <summary>
/// Phase UI-6 code/XAML contracts: one wording for each concept, one entry per result action,
/// and the tray menu sharing MainWindow's action availability.
/// </summary>
public sealed class DailyUseContractTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex JapaneseRegex = new("[\u3040-\u30FF\u3400-\u9FFF\uFF00-\uFFEF]", RegexOptions.Compiled);

    // Terms that must not appear in user-facing text (glossary, visual_direction.md §23).
    private static readonly string[] ForbiddenTerms =
    [
        "raw",
        "不採用候補",
        "もう一度整形",
        "手動採用",
        "直近結果",
        "Gemma",
        "llm.enabled",
        "文章を整えています",
        "FeatherScribeの前に使っていたウィンドウ",
    ];

    [Fact]
    public void UserFacingText_DoesNotUseForbiddenTerms()
    {
        var violations = new List<string>();
        foreach (var (file, text) in EnumerateJapaneseUserTexts())
        {
            // The only exception: startup tray notices intentionally point to the settings file.
            if (text.Contains("config/appsettings.json", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var term in ForbiddenTerms)
            {
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{file}: \"{text}\" contains \"{term}\"");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void GlossaryScan_FindsUserFacingTexts()
    {
        // Guards the scanner itself: it must see the C# and XAML texts it is meant to check.
        var texts = EnumerateJapaneseUserTexts().Select(item => item.Text).ToArray();

        Assert.Contains("整形中…", texts);
        Assert.Contains("直近の結果を直前の入力先へ貼り付け", texts);
        Assert.Contains("整形候補を採用", texts);
        Assert.Contains("再整形", texts);
    }

    [Fact]
    public void MainWindowXaml_UsesSpecifiedTexts()
    {
        var document = LoadXaml("MainWindow.xaml");

        AssertAttribute(document, "ReformatButton", "Content", "再整形");
        AssertAttribute(document, "ReformatButton", "ToolTip", "直近の未整形の文章を、バックグラウンドで整形し直します。");
        AssertAttribute(document, "AdoptRejectedButton", "Content", "整形候補を採用");
        AssertAttribute(document, "AdoptRejectedButton", "ToolTip", "確認した整形候補を、直近の結果として採用します。");
        AssertAttribute(document, "RepasteButton", "Content", "直前の入力先へ貼り付け");
        AssertAttribute(document, "RepasteButton", "ToolTip", "直近の結果をクリップボードに入れてから、直前の入力先へ貼り付けます。");
        AssertAttribute(document, "RecopyButton", "Content", "クリップボードにコピー");
        AssertAttribute(document, "RecopyButton", "ToolTip", "直近の結果をクリップボードに入れます。貼り付けはしません。");
        AssertAttribute(document, "StatusText", "Text", "待機中");

        var texts = document.Descendants().Select(element => element.Attribute("Text")?.Value).ToArray();
        Assert.Contains("自動では採用しなかった整形候補です。確認して、必要な場合だけ採用できます。", texts);
        Assert.Contains("直近の結果", texts);
        Assert.Contains("コピー・貼り付け対象", texts);
        Assert.Contains("整形候補・警告", texts);
        Assert.Contains("操作ガイド", texts);
    }

    [Fact]
    public void MainWindowXaml_ActionButtonsStartDisabled()
    {
        var document = LoadXaml("MainWindow.xaml");

        foreach (var name in new[] { "ReformatButton", "AdoptRejectedButton", "RecopyButton", "RepasteButton" })
        {
            AssertAttribute(document, name, "IsEnabled", "False");
        }
    }

    [Fact]
    public void MainWindow_HasOneEntryPerActionAndClickHandlersCallIt()
    {
        var code = ReadAppFile("MainWindow.xaml.cs");

        Assert.Contains("public async Task CopyLatestAsync()", code);
        Assert.Contains("public async Task PasteLatestToPreviousTargetAsync()", code);
        Assert.Contains("public void StartReformat()", code);
        Assert.Contains("public void AdoptCandidate()", code);

        // The previous public names are gone, so there is exactly one public entry per action.
        foreach (var oldName in new[] { "RecopyAsync", "RepasteAsync", "void ReformatLast(", "AdoptRejectedResult(" })
        {
            Assert.DoesNotContain(oldName, code);
        }

        AssertHandlerCalls(code, "RecopyButton_Click", "CopyLatestAsync()");
        AssertHandlerCalls(code, "RepasteButton_Click", "PasteLatestToPreviousTargetAsync()");
        AssertHandlerCalls(code, "ReformatButton_Click", "StartReformat()");
        AssertHandlerCalls(code, "AdoptRejectedButton_Click", "AdoptCandidate()");

        // Each entry catches its own exceptions and reports through In-App Feedback.
        foreach (var entry in new[]
        {
            "public async Task CopyLatestAsync()",
            "public async Task PasteLatestToPreviousTargetAsync()",
            "public void StartReformat()",
            "public void AdoptCandidate()",
        })
        {
            var body = MethodBody(code, entry);
            Assert.Contains("catch (Exception ex)", body);
            Assert.Contains("Debug.WriteLine(", body);
            Assert.Contains("InAppFeedbackKind.Error", body);
            Assert.Contains("RefreshActionAvailability();", body);
        }
    }

    [Fact]
    public void MainWindow_ActionAvailabilityHasSingleSource()
    {
        var code = ReadAppFile("MainWindow.xaml.cs");

        Assert.Contains("internal ActionAvailability GetActionAvailability() => ActionAvailability.From(_controller);", code);
        Assert.Contains("RecopyButton.IsEnabled = availability.CanCopy;", code);
        Assert.Contains("RepasteButton.IsEnabled = availability.CanRepaste;", code);
        Assert.Contains("ReformatButton.IsEnabled = availability.CanReformat;", code);
        Assert.Contains("AdoptRejectedButton.IsEnabled = availability.CanAdopt;", code);
        Assert.DoesNotMatch(new Regex(@"\.IsEnabled\s*=\s*(true|false)"), code);

        foreach (var method in new[]
        {
            "public void UpdateStage(",
            "public void UpdateResult(",
            "public void UpdateBackgroundFormatting(",
        })
        {
            Assert.Contains("RefreshActionAvailability();", MethodBody(code, method));
        }
    }

    [Fact]
    public void MainWindow_UsesTrackerOwnedByApp()
    {
        var mainWindow = ReadAppFile("MainWindow.xaml.cs");
        var app = ReadAppFile("App.xaml.cs");

        Assert.DoesNotContain("new ForegroundWindowTracker", mainWindow);
        Assert.DoesNotContain("_foregroundWindowTracker.Dispose()", mainWindow);
        Assert.Single(Regex.Matches(app, @"new ForegroundWindowTracker\("));
        Assert.Contains("_foregroundWindowTracker?.Dispose();", app);
        Assert.Contains("new MainWindow(controller, settings, textOutput, foregroundWindowTracker)", app);
    }

    [Fact]
    public void App_PipelineUsesPasteGuardAndMainWindowUsesInnerOutput()
    {
        var app = ReadAppFile("App.xaml.cs");

        Assert.Contains("new PasteTargetGuardTextOutput(", app);
        Assert.Contains("ForegroundWindowTracker.IsCurrentProcessForeground()", app);
        Assert.Contains("foregroundWindowTracker.TryRestoreLastExternalWindow()", app);
        Assert.Contains("Dispatcher.Invoke(", app);

        var pipelineConstruction = app[app.IndexOf("new DictationPipeline(", StringComparison.Ordinal)..];
        pipelineConstruction = pipelineConstruction[..pipelineConstruction.IndexOf(';')];
        Assert.Contains("pipelineOutput", pipelineConstruction);
        Assert.DoesNotContain("textOutput", pipelineConstruction);
    }

    [Fact]
    public void App_NotificationsUseUserFacingText()
    {
        var app = ReadAppFile("App.xaml.cs");

        Assert.DoesNotContain("ToDisplayReason", app);
        Assert.DoesNotContain("FormatBackgroundFormattingFailure", app);
        // Completion notices come from one pure helper (order: failure → paste failed → fallback).
        var completed = app[app.IndexOf("controller.Completed +=", StringComparison.Ordinal)..];
        completed = completed[..completed.IndexOf("});", StringComparison.Ordinal)];
        Assert.Contains("UserFacingText.CompletionNotice(result) is { } notice", completed);
        Assert.Contains("_trayIconService!.Notify(notice.Title, notice.Body);", completed);
        Assert.Single(Regex.Matches(completed, @"\.Notify\("));
        foreach (var key in new[] { "NotifyFailureTitle", "NotifyFallbackTitle", "NotifyOutputFailedTitle" })
        {
            Assert.DoesNotContain(key, app);
        }

        foreach (var key in new[]
        {
            "NotifyBackgroundFormattedTitle",
            "NotifyBackgroundRejectedTitle",
            "NotifyBackgroundFailedTitle",
        })
        {
            Assert.Contains($"UserFacingText.{key}", app);
        }
    }

    [Fact]
    public void Tray_MenuHandlersCallMainWindowEntriesWithoutSafeAsync()
    {
        var tray = ReadAppFile("TrayIconService.cs");

        Assert.DoesNotContain("SafeAsync", tray);
        Assert.Contains("(_, _) => _mainWindow.StartReformat()", tray);
        Assert.Contains("async (_, _) => await _mainWindow.CopyLatestAsync()", tray);
        Assert.Contains("async (_, _) => await _mainWindow.PasteLatestToPreviousTargetAsync()", tray);
        Assert.Contains("(_, _) => ShowMainWindow()", tray);
        Assert.Contains("(_, _) => ExitApplication()", tray);

        // Every menu item is added with a handler that is one of the calls above.
        var handlers = Regex.Matches(tray, @"menu\.Items\.Add\(UserFacingText\.\w+, null, (?<handler>[^;]+)\);")
            .Select(match => match.Groups["handler"].Value)
            .ToArray();
        Assert.Equal(5, handlers.Length);
        Assert.All(handlers, handler => Assert.Matches(
            @"^(async )?\(_, _\) => (await )?(_mainWindow\.(StartReformat|CopyLatestAsync|PasteLatestToPreviousTargetAsync)\(\)|ShowMainWindow\(\)|ExitApplication\(\))$",
            handler));

        // No adopt item in the tray.
        Assert.DoesNotContain("AdoptCandidate", tray);
    }

    [Fact]
    public void Tray_OpeningHandlerSetsEnabledFromMainWindowAvailability()
    {
        var tray = ReadAppFile("TrayIconService.cs");

        Assert.Contains("menu.Opening += (_, _) => RefreshMenuAvailability();", tray);
        var refresh = MethodBody(tray, "private void RefreshMenuAvailability()");
        Assert.Contains("_mainWindow.GetActionAvailability()", refresh);
        Assert.Contains("_reformatItem.Enabled = availability.CanReformat;", refresh);
        Assert.Contains("_copyItem.Enabled = availability.CanCopy;", refresh);
        Assert.Contains("_pasteItem.Enabled = availability.CanRepaste;", refresh);
    }

    [Fact]
    public void DictationController_ReformatLastUsesSharedPredicate()
    {
        var code = ReadAppFile("DictationController.cs");

        Assert.Equal(3, Regex.Matches(code, @"CanReformatCore\(").Count); // definition, CanReformat, ReformatLast
        Assert.Contains("if (!CanReformatCore(out var raw, out var mode))", MethodBody(code, "public FormattingMode? ReformatLast()"));
    }

    private static void AssertHandlerCalls(string code, string handler, string entryCall)
    {
        var start = code.IndexOf($"void {handler}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{handler} not found");
        var end = code.IndexOf(';', start);
        Assert.Contains(entryCall, code[start..end]);
    }

    private static string MethodBody(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var open = code.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{')
            {
                depth++;
            }
            else if (code[i] == '}' && --depth == 0)
            {
                return code[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body: {signature}");
    }

    private static void AssertAttribute(XDocument document, string name, string attribute, string expected)
    {
        var element = document.Descendants()
            .FirstOrDefault(candidate => candidate.Attribute(XamlNamespace + "Name")?.Value == name)
            ?? throw new InvalidOperationException($"Missing element: {name}");
        Assert.Equal(expected, element.Attribute(attribute)?.Value);
    }

    /// <summary>Japanese string literals in App/*.cs and Japanese attribute values in App/*.xaml.</summary>
    private static IEnumerable<(string File, string Text)> EnumerateJapaneseUserTexts()
    {
        var appDirectory = Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App");

        foreach (var path in Directory.GetFiles(appDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            foreach (var literal in ExtractStringLiterals(File.ReadAllText(path)))
            {
                if (JapaneseRegex.IsMatch(literal))
                {
                    yield return (Path.GetFileName(path), literal);
                }
            }
        }

        foreach (var path in Directory.GetFiles(appDirectory, "*.xaml", SearchOption.TopDirectoryOnly))
        {
            foreach (var attribute in XDocument.Load(path).Descendants().Attributes())
            {
                if (JapaneseRegex.IsMatch(attribute.Value))
                {
                    yield return (Path.GetFileName(path), attribute.Value);
                }
            }
        }
    }

    /// <summary>Minimal C# lexer: string literals (regular, interpolated, verbatim), skipping comments and char literals.</summary>
    private static IEnumerable<string> ExtractStringLiterals(string code)
    {
        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                i = code.IndexOf('\n', i) is var lineEnd and >= 0 ? lineEnd : code.Length;
                continue;
            }

            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                var close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close >= 0 ? close + 2 : code.Length;
                continue;
            }

            if (c == '\'')
            {
                i++;
                while (i < code.Length && code[i] != '\'')
                {
                    i += code[i] == '\\' ? 2 : 1;
                }

                i++;
                continue;
            }

            if (c == '"')
            {
                var verbatim = i > 0 && (code[i - 1] == '@' || (i > 1 && code[i - 1] == '$' && code[i - 2] == '@'));
                var builder = new StringBuilder();
                i++;
                while (i < code.Length)
                {
                    if (verbatim && code[i] == '"' && i + 1 < code.Length && code[i + 1] == '"')
                    {
                        builder.Append('"');
                        i += 2;
                        continue;
                    }

                    if (!verbatim && code[i] == '\\' && i + 1 < code.Length)
                    {
                        builder.Append(code[i + 1] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', var other => other });
                        i += 2;
                        continue;
                    }

                    if (code[i] == '"')
                    {
                        break;
                    }

                    builder.Append(code[i]);
                    i++;
                }

                i++;
                yield return builder.ToString();
                continue;
            }

            i++;
        }
    }

    private static XDocument LoadXaml(string fileName)
        => XDocument.Load(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", fileName));

    private static string ReadAppFile(string fileName)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", fileName));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FeatherScribe.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FeatherScribe repository root.");
    }
}
