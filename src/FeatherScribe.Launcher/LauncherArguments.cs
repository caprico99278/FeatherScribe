namespace FeatherScribe.Launcher;

/// <summary>
/// Command line of the launcher. <c>--app &lt;path&gt;</c> comes from FeatherScribe.cmd (relative to the
/// repository root); <c>--llm on|off</c> skips the yes/no prompt and <c>--model &lt;tag&gt;</c> skips the
/// model menu (for shortcuts and tests). Unknown or incomplete switches are reported and ignored.
/// </summary>
internal sealed record LauncherArguments(string? AppPath, bool? Llm, string? Model, IReadOnlyList<string> Warnings)
{
    public static LauncherArguments Parse(IReadOnlyList<string> args)
    {
        string? appPath = null;
        bool? llm = null;
        string? model = null;
        var warnings = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg.ToLowerInvariant())
            {
                case "--app":
                    if (TryTakeValue(args, ref index, out var app))
                    {
                        appPath = app;
                    }
                    else
                    {
                        warnings.Add("--app に値がありません。");
                    }

                    break;

                case "--llm":
                    if (TryTakeValue(args, ref index, out var llmValue) && TryParseOnOff(llmValue, out var enabled))
                    {
                        llm = enabled;
                    }
                    else
                    {
                        warnings.Add("--llm には on または off を指定してください（指定を無視しました）。");
                    }

                    break;

                case "--model":
                    if (TryTakeValue(args, ref index, out var tag))
                    {
                        model = tag;
                    }
                    else
                    {
                        warnings.Add("--model にモデル名がありません（指定を無視しました）。");
                    }

                    break;

                default:
                    warnings.Add($"不明な引数を無視しました: {arg}");
                    break;
            }
        }

        return new LauncherArguments(appPath, llm, model, warnings);
    }

    public static bool TryParseOnOff(string? value, out bool enabled)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "on":
                enabled = true;
                return true;
            case "off":
                enabled = false;
                return true;
            default:
                enabled = false;
                return false;
        }
    }

    // The next argument is the value unless it is missing, blank or another switch.
    private static bool TryTakeValue(IReadOnlyList<string> args, ref int index, out string value)
    {
        if (index + 1 < args.Count &&
            !string.IsNullOrWhiteSpace(args[index + 1]) &&
            !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            index++;
            value = args[index].Trim();
            return true;
        }

        value = "";
        return false;
    }
}
