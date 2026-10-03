using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Launcher;

/// <summary>
/// Console launcher run by FeatherScribe.cmd (Phase LAUNCH-2): asks whether to use LLM formatting; when yes,
/// makes sure Ollama is running, lets the user pick an installed model, waits until it is loaded, and then
/// starts FeatherScribe.App.exe with command-line overrides. Config files are never written. Any LLM
/// failure offers to start without LLM formatting, so the user is not left without a working app.
/// </summary>
internal static class Program
{
    private const string DryRunVariable = "FEATHERSCRIBE_LAUNCH_DRY_RUN";

    private static readonly TimeSpan ServerStartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ServerPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ModelLoadTimeout = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    private static async Task<int> Main(string[] args)
    {
        TryUseUtf8Output();

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return await RunAsync(args, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.WriteLine();
            Console.WriteLine("中断しました。FeatherScribeは起動していません。");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var arguments = LauncherArguments.Parse(args);
        foreach (var warning in arguments.Warnings)
        {
            Console.WriteLine(warning);
        }

        if (arguments.AppPath is null)
        {
            Console.WriteLine("起動するアプリが指定されていません（--app）。FeatherScribe.cmd から起動してください。");
            return 1;
        }

        var rootPath = AppRoot.Locate();
        var app = new AppStarter(
            Path.GetFullPath(Path.Combine(rootPath, arguments.AppPath)),
            arguments.AppPath,
            rootPath,
            Environment.GetEnvironmentVariable(DryRunVariable) == "1");
        if (!File.Exists(app.ExecutablePath))
        {
            Console.WriteLine($"FeatherScribe.App.exe が見つかりません: {arguments.AppPath}");
            return 1;
        }

        var llm = new JsonAppSettingsProvider(rootPath).Load().Llm;

        var useLlm = arguments.Llm ?? AskYesNo(
            $"LLM整形を使いますか？ {LaunchPrompt.YesNoHint(llm.Enabled)}（Enter = {(llm.Enabled ? "使う" : "使わない")}）",
            llm.Enabled,
            cancellationToken);
        if (!useLlm)
        {
            return app.Start(AppLaunchArguments.Build(llmEnabled: false, model: null, ownedOllamaPid: null));
        }

        Process? ownedServer = null;
        try
        {
            var preparation = await PrepareLlmAsync(
                rootPath, llm, arguments.Model, app.DryRun, server => ownedServer = server, cancellationToken).ConfigureAwait(false);

            if (preparation.Model is { } model)
            {
                if (preparation.WouldStartServer)
                {
                    Console.WriteLine("DRY RUN: the app would also get --owned-ollama-pid <pid of the started Ollama>");
                }

                var exitCode = app.Start(AppLaunchArguments.Build(llmEnabled: true, model, ownedServer?.Id));
                if (exitCode == 0)
                {
                    // Handed over: FeatherScribe stops this server when it exits.
                    ownedServer = null;
                }

                return exitCode;
            }

            Console.WriteLine(preparation.FailureReason);
            if (!AskYesNo("LLM整形を使わずに起動しますか？ [Y/n]", defaultValue: true, cancellationToken))
            {
                Console.WriteLine("起動を中止しました。");
                return 1;
            }

            StopOwnedServer(ref ownedServer);
            return app.Start(AppLaunchArguments.Build(llmEnabled: false, model: null, ownedOllamaPid: null));
        }
        finally
        {
            // A server this launcher started but did not hand to the app is not left running.
            StopOwnedServer(ref ownedServer);
        }
    }

    private static async Task<LlmPreparation> PrepareLlmAsync(
        string rootPath,
        LlmSettings llm,
        string? requestedModel,
        bool dryRun,
        Action<Process> serverStarted,
        CancellationToken cancellationToken)
    {
        OllamaClient client;
        try
        {
            client = new OllamaClient(llm.Endpoint);
        }
        catch (UriFormatException)
        {
            return LlmPreparation.Failed($"llm.endpoint の形式が正しくありません: {llm.Endpoint}");
        }

        using (client)
        {
            var keepAlive = LaunchDefaults.EffectiveKeepAlive(llm.KeepAlive);

            if (await client.IsRunningAsync(cancellationToken).ConfigureAwait(false))
            {
                Console.WriteLine("起動済みのOllamaを使います。");
            }
            else
            {
                if (!File.Exists(OllamaServer.ExecutablePath(rootPath)))
                {
                    return LlmPreparation.Failed(
                        @"Ollamaが起動しておらず、local\ollama\ollama.exe も見つかりません（README: セットアップ）。");
                }

                if (!Directory.Exists(OllamaServer.ModelsDirectory(rootPath)))
                {
                    return LlmPreparation.Failed(@"Ollamaのモデルフォルダ local\ollama-models が見つかりません（README: セットアップ）。");
                }

                if (dryRun)
                {
                    // Nothing to ask without a server: the dry run ends with the model it would load.
                    Console.WriteLine("DRY RUN: would start Ollama");
                    var dryRunModel = requestedModel ?? llm.Model;
                    Console.WriteLine($"DRY RUN: would load {dryRunModel} (keep_alive={keepAlive})");
                    return LlmPreparation.Ready(dryRunModel, wouldStartServer: true);
                }

                Process server;
                try
                {
                    server = OllamaServer.Start(rootPath);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    return LlmPreparation.Failed($"Ollamaを起動できませんでした: {ex.Message}");
                }

                serverStarted(server);
                if (await WaitForServerAsync(client, server, cancellationToken).ConfigureAwait(false) is { } startFailure)
                {
                    return LlmPreparation.Failed(startFailure);
                }

                Console.WriteLine("Ollamaを起動しました。");
            }

            IReadOnlyList<OllamaModel> models;
            try
            {
                models = await client.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
            {
                return LlmPreparation.Failed($"モデル一覧を取得できませんでした: {ex.Message}");
            }

            if (models.Count == 0)
            {
                return LlmPreparation.Failed("インストール済みのモデルがありません（README: セットアップ「Gemma 4 / Ollama」）。");
            }

            string model;
            if (requestedModel is not null)
            {
                if (LaunchPrompt.FindModelIndex(models, requestedModel) is not { } requestedIndex)
                {
                    return LlmPreparation.Failed($"指定されたモデル {requestedModel} はインストールされていません。");
                }

                model = models[requestedIndex].Name;
                Console.WriteLine($"モデル: {model}");
            }
            else
            {
                model = models[ChooseModel(models, llm.Model, cancellationToken)].Name;
            }

            if (dryRun)
            {
                Console.WriteLine($"DRY RUN: would load {model} (keep_alive={keepAlive})");
                return LlmPreparation.Ready(model, wouldStartServer: false);
            }

            var requestJson = OllamaApi.BuildLoadRequestJson(model, keepAlive, llm.GpuLayers, llm.NumContext);
            if (await LoadModelAsync(client, model, requestJson, cancellationToken).ConfigureAwait(false) is { } loadFailure)
            {
                return LlmPreparation.Failed(loadFailure);
            }

            return LlmPreparation.Ready(model, wouldStartServer: false);
        }
    }

    /// <summary>Polls /api/version every 500 ms up to 60 s. Null when the server answers, otherwise the reason.</summary>
    private static async Task<string?> WaitForServerAsync(OllamaClient client, Process server, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var progress = new ProgressLine();
        while (stopwatch.Elapsed < ServerStartTimeout)
        {
            progress.Update($"Ollamaを起動しています… {(int)stopwatch.Elapsed.TotalSeconds}秒");
            if (await client.IsRunningAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            if (server.HasExited)
            {
                return "Ollamaが起動直後に終了しました（ポートが他のプロセスに使われている可能性があります）。";
            }

            await Task.Delay(ServerPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return $"Ollamaが{(int)ServerStartTimeout.TotalSeconds}秒以内に応答しませんでした。";
    }

    /// <summary>Loads the model and prints the elapsed seconds every second. Null on success, otherwise the reason.</summary>
    private static async Task<string?> LoadModelAsync(
        OllamaClient client, string model, string requestJson, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ModelLoadTimeout);

        var stopwatch = Stopwatch.StartNew();
        var load = client.LoadModelAsync(requestJson, timeout.Token);
        using (var progress = new ProgressLine())
        {
            while (true)
            {
                progress.Update($"{model} を読み込んでいます… {(int)stopwatch.Elapsed.TotalSeconds}秒");
                if (await Task.WhenAny(load, Task.Delay(ProgressInterval, cancellationToken)).ConfigureAwait(false) == load)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        try
        {
            if (await load.ConfigureAwait(false) is { } error)
            {
                return $"モデル {model} を読み込めませんでした: {error}";
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"モデル {model} の読み込みが{(int)ModelLoadTimeout.TotalSeconds}秒以内に終わりませんでした。";
        }

        Console.WriteLine($"{model} を読み込みました（{(int)stopwatch.Elapsed.TotalSeconds}秒）。");
        return null;
    }

    private static int ChooseModel(IReadOnlyList<OllamaModel> models, string configuredModel, CancellationToken cancellationToken)
    {
        var configuredIndex = LaunchPrompt.FindModelIndex(models, configuredModel);
        var defaultNumber = LaunchPrompt.DefaultModelIndex(models, configuredModel) + 1;
        var nameWidth = models.Max(model => model.Name.Length);

        Console.WriteLine("使用するLLMを選んでください:");
        for (var index = 0; index < models.Count; index++)
        {
            var line = $"  {index + 1}) {models[index].Name.PadRight(nameWidth)}  {LaunchPrompt.FormatSize(models[index].SizeBytes),8}";
            Console.WriteLine(index == configuredIndex ? line + "  （既定）" : line);
        }

        while (true)
        {
            Console.Write($"番号を入力してください（Enter = {defaultNumber}）: ");
            var input = ReadLine(cancellationToken);
            if (LaunchPrompt.ParseModelChoice(input, models, configuredModel) is { } choice)
            {
                return choice;
            }

            Console.WriteLine($"1〜{models.Count} の番号を入力してください。");
        }
    }

    private static bool AskYesNo(string prompt, bool defaultValue, CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.Write(prompt + " ");
            if (LaunchPrompt.ParseYesNo(ReadLine(cancellationToken), defaultValue) is { } answer)
            {
                return answer;
            }

            Console.WriteLine("y または n を入力してください。");
        }
    }

    // End of input (null) counts as Enter; Ctrl+C while waiting for input ends the launcher.
    private static string? ReadLine(CancellationToken cancellationToken)
    {
        var input = Console.ReadLine();
        cancellationToken.ThrowIfCancellationRequested();
        if (input is null)
        {
            Console.WriteLine();
        }

        return input;
    }

    private static void StopOwnedServer(ref Process? server)
    {
        if (server is null)
        {
            return;
        }

        OllamaServer.Stop(server);
        server.Dispose();
        server = null;
    }

    private static void TryUseUtf8Output()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            Debug.WriteLine($"[Launcher] console output encoding unchanged: {ex.GetType().Name}");
        }
    }

    private sealed record LlmPreparation(string? Model, string? FailureReason, bool WouldStartServer)
    {
        public static LlmPreparation Ready(string model, bool wouldStartServer) => new(model, null, wouldStartServer);

        public static LlmPreparation Failed(string reason) => new(null, reason, false);
    }

    /// <summary>Starts FeatherScribe.App.exe (or prints the command line in a dry run).</summary>
    private sealed record AppStarter(string ExecutablePath, string DisplayPath, string RootPath, bool DryRun)
    {
        public int Start(IReadOnlyList<string> args)
        {
            if (DryRun)
            {
                Console.WriteLine($"DRY RUN: would start {AppLaunchArguments.FormatCommandLine(DisplayPath, args)}");
                return 0;
            }

            var startInfo = new ProcessStartInfo(ExecutablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = RootPath,
            };
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    Console.WriteLine("FeatherScribeを起動できませんでした。");
                    return 1;
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                Console.WriteLine($"FeatherScribeを起動できませんでした: {ex.Message}");
                return 1;
            }

            Console.WriteLine("FeatherScribeを起動しました。");
            return 0;
        }
    }

    /// <summary>One console line rewritten in place (carriage return); ends with a newline when disposed.</summary>
    private sealed class ProgressLine : IDisposable
    {
        private int _lastLength;

        public void Update(string text)
        {
            Console.Write("\r" + text + new string(' ', Math.Max(0, _lastLength - text.Length)));
            _lastLength = text.Length;
        }

        public void Dispose()
        {
            if (_lastLength > 0)
            {
                Console.WriteLine();
            }
        }
    }
}
