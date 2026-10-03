using System.Globalization;
using System.Text;
using FeatherScribe.Core;

namespace FeatherScribe.Launcher;

/// <summary>Arguments of FeatherScribe.App.exe set by the launcher (parsed by the app's LaunchOverrides).</summary>
internal static class AppLaunchArguments
{
    /// <summary>
    /// <c>--llm on|off</c>; with LLM on and a model, <c>--llm-model &lt;tag&gt;</c>; and
    /// <c>--owned-ollama-pid &lt;pid&gt;</c> when the launcher started the Ollama server the app stops on exit.
    /// </summary>
    public static IReadOnlyList<string> Build(bool llmEnabled, string? model, int? ownedOllamaPid)
    {
        var args = new List<string> { "--llm", llmEnabled ? "on" : "off" };
        if (llmEnabled && !string.IsNullOrWhiteSpace(model))
        {
            args.Add("--llm-model");
            args.Add(model.Trim());
        }

        if (ownedOllamaPid is > 0)
        {
            args.Add("--owned-ollama-pid");
            args.Add(ownedOllamaPid.Value.ToString(CultureInfo.InvariantCulture));
        }

        return args;
    }

    /// <summary>The command line as Windows receives it (printed by the dry run).</summary>
    public static string FormatCommandLine(string executable, IEnumerable<string> args)
        => string.Join(' ', new[] { executable }.Concat(args).Select(Quote));

    /// <summary>
    /// Quotes one argument by the Windows command-line rules: kept as is unless it is empty or contains a
    /// space, tab or double quote; inside quotes, double quotes and the backslashes before them or before
    /// the closing quote are escaped.
    /// </summary>
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            builder.Append('\\', character == '"' ? (backslashes * 2) + 1 : backslashes);
            backslashes = 0;
            builder.Append(character);
        }

        builder.Append('\\', backslashes * 2);
        return builder.Append('"').ToString();
    }
}

/// <summary>Defaults shared with the app when the launcher enables LLM formatting.</summary>
internal static class LaunchDefaults
{
    /// <summary>The configured llm.keepAlive, or <see cref="LlmSettings.LauncherDefaultKeepAlive"/> when it is not set.</summary>
    public static string EffectiveKeepAlive(string? configured)
        => string.IsNullOrWhiteSpace(configured) ? LlmSettings.LauncherDefaultKeepAlive : configured.Trim();
}
