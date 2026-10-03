using System.Diagnostics;
using System.Globalization;
using System.IO;
using FeatherScribe.Core;

namespace FeatherScribe.App;

/// <summary>
/// Command-line overrides set by the launcher (FeatherScribe.cmd, Phase LAUNCH-2), applied in memory only
/// (config files are never written): <c>--llm on|off</c>, <c>--llm-model &lt;tag&gt;</c> and
/// <c>--owned-ollama-pid &lt;pid&gt;</c> (the Ollama server the launcher started, stopped on exit).
/// Unknown or invalid arguments are ignored with a Debug line.
/// </summary>
internal sealed record LaunchOverrides(bool? LlmEnabled, string? LlmModel, int? OwnedOllamaPid)
{
    public static LaunchOverrides None { get; } = new(null, null, null);

    public static LaunchOverrides Parse(IReadOnlyList<string> args)
    {
        bool? llmEnabled = null;
        string? llmModel = null;
        int? ownedOllamaPid = null;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg.ToLowerInvariant())
            {
                case "--llm":
                    if (TryTakeValue(args, ref index, out var llmValue) && TryParseOnOff(llmValue, out var enabled))
                    {
                        llmEnabled = enabled;
                    }
                    else
                    {
                        Debug.WriteLine("[App] ignored --llm: expected on or off");
                    }

                    break;

                case "--llm-model":
                    if (TryTakeValue(args, ref index, out var model))
                    {
                        llmModel = model;
                    }
                    else
                    {
                        Debug.WriteLine("[App] ignored --llm-model: missing model");
                    }

                    break;

                case "--owned-ollama-pid":
                    if (TryTakeValue(args, ref index, out var pidText) &&
                        int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) &&
                        pid > 0)
                    {
                        ownedOllamaPid = pid;
                    }
                    else
                    {
                        Debug.WriteLine("[App] ignored --owned-ollama-pid: expected a positive process id");
                    }

                    break;

                default:
                    Debug.WriteLine("[App] ignored unknown argument");
                    break;
            }
        }

        return new LaunchOverrides(llmEnabled, llmModel, ownedOllamaPid);
    }

    /// <summary>
    /// The settings with the overrides applied: llm.enabled and llm.model replaced when given, and when the
    /// launcher enabled LLM formatting without a configured llm.keepAlive, keepAlive =
    /// <see cref="LlmSettings.LauncherDefaultKeepAlive"/> (the launcher preloads the model with the same value).
    /// </summary>
    public AppSettings Apply(AppSettings settings)
    {
        if (LlmEnabled is null && LlmModel is null)
        {
            return settings;
        }

        var llm = settings.Llm;
        var keepAlive = LlmEnabled == true && string.IsNullOrWhiteSpace(llm.KeepAlive)
            ? LlmSettings.LauncherDefaultKeepAlive
            : llm.KeepAlive;

        return SettingsCopy.WithLlm(
            settings,
            SettingsCopy.WithLlmOverrides(llm, LlmEnabled ?? llm.Enabled, LlmModel ?? llm.Model, keepAlive));
    }

    private static bool TryParseOnOff(string value, out bool enabled)
    {
        switch (value.ToLowerInvariant())
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

/// <summary>Copies of the init-only settings objects with some values replaced (JsonAppSettingsProvider stays unchanged).</summary>
internal static class SettingsCopy
{
    public static AppSettings WithLlm(AppSettings settings, LlmSettings llm)
        => new()
        {
            Asr = settings.Asr,
            Llm = llm,
            Recording = settings.Recording,
            Output = settings.Output,
            Hotkeys = settings.Hotkeys,
            SelectionEdit = settings.SelectionEdit,
            Privacy = settings.Privacy,
            Debug = settings.Debug,
        };

    public static LlmSettings WithLlmOverrides(LlmSettings llm, bool enabled, string model, string? keepAlive)
        => new()
        {
            Enabled = enabled,
            Provider = llm.Provider,
            Endpoint = llm.Endpoint,
            Model = model,
            QualityModel = llm.QualityModel,
            Temperature = llm.Temperature,
            NumPredict = llm.NumPredict,
            NumContext = llm.NumContext,
            KeepAlive = keepAlive,
            TimeoutSeconds = llm.TimeoutSeconds,
            QualityTimeoutSeconds = llm.QualityTimeoutSeconds,
            FallbackToRaw = llm.FallbackToRaw,
            RawFirstPaste = llm.RawFirstPaste,
            GpuLayers = llm.GpuLayers,
        };
}

/// <summary>
/// Stops the Ollama server the launcher started (<c>--owned-ollama-pid</c>) when FeatherScribe exits, only
/// while that process id still belongs to the repository's <c>local/ollama/ollama.exe</c>.
/// </summary>
internal static class OwnedOllamaProcess
{
    public const string ExpectedProcessName = "ollama";

    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(2);

    public static string ExpectedExecutablePath(string rootPath)
        => Path.GetFullPath(Path.Combine(rootPath, "local", "ollama", "ollama.exe"));

    /// <summary>
    /// True only when the process is named <c>ollama</c> and its executable is exactly the expected full
    /// path (case-insensitive), so a reused process id or another Ollama installation is never stopped.
    /// </summary>
    public static bool IsOwnedOllama(string? processName, string? executablePath, string expectedExecutablePath)
        => string.Equals(processName, ExpectedProcessName, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(executablePath) &&
            !string.IsNullOrWhiteSpace(expectedExecutablePath) &&
            string.Equals(executablePath, expectedExecutablePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Stops the process tree when <see cref="IsOwnedOllama"/> holds; waits at most ~2 s. Never throws.
    /// Returns null on success, otherwise a short error type for the event log.
    /// </summary>
    public static string? TryStop(int pid, string expectedExecutablePath)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return "not_running";
            }

            if (!IsOwnedOllama(process.ProcessName, process.MainModule?.FileName, expectedExecutablePath))
            {
                Debug.WriteLine("[App] owned Ollama pid now belongs to another process; not stopped");
                return "not_owned";
            }

            process.Kill(entireProcessTree: true);
            return process.WaitForExit(StopWait) ? null : "stop_timeout";
        }
        catch (ArgumentException)
        {
            // No process with this id: the server has already exited.
            return "not_running";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] stopping owned Ollama failed: {ex.GetType().Name}");
            return ex.GetType().Name;
        }
    }
}
