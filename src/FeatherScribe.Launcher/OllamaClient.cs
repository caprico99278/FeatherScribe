using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace FeatherScribe.Launcher;

/// <summary>The few Ollama HTTP calls of the launcher. Timeouts are per call (the HttpClient has none).</summary>
internal sealed class OllamaClient : IDisposable
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TagsTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;

    /// <exception cref="UriFormatException">The endpoint is not an absolute URI.</exception>
    public OllamaClient(string endpoint)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.Trim().TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>True when <c>GET /api/version</c> answers with success within 2 seconds.</summary>
    public async Task<bool> IsRunningAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(VersionTimeout);
        try
        {
            using var response = await _httpClient.GetAsync(OllamaApi.VersionPath, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Debug.WriteLine($"[Launcher] Ollama not answering: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>Installed models (<c>GET /api/tags</c>).</summary>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    /// <exception cref="TimeoutException">No answer within 10 seconds.</exception>
    public async Task<IReadOnlyList<OllamaModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TagsTimeout);
        try
        {
            using var response = await _httpClient.GetAsync(OllamaApi.TagsPath, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return OllamaTags.Parse(json);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Ollama did not answer /api/tags within 10 seconds.");
        }
    }

    /// <summary>
    /// Sends the model load request (<see cref="OllamaApi.BuildLoadRequestJson"/>) and waits until Ollama has
    /// loaded the model. Returns null on success, otherwise the reason. Cancellation is the caller's timeout.
    /// </summary>
    public async Task<string?> LoadModelAsync(string requestJson, CancellationToken cancellationToken)
    {
        using var content = new StringContent(requestJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            using var response = await _httpClient.PostAsync(OllamaApi.GeneratePath, content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return OllamaApi.ParseError(body);
            }

            return OllamaApi.ParseError(body) ?? $"HTTP {(int)response.StatusCode}";
        }
        catch (HttpRequestException ex)
        {
            return ex.Message;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

/// <summary>The repository-local Ollama server (<c>local/ollama/ollama.exe serve</c>, models in <c>local/ollama-models</c>).</summary>
internal static class OllamaServer
{
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(2);

    public static string ExecutablePath(string rootPath)
        => Path.GetFullPath(Path.Combine(rootPath, "local", "ollama", "ollama.exe"));

    public static string ModelsDirectory(string rootPath)
        => Path.GetFullPath(Path.Combine(rootPath, "local", "ollama-models"));

    /// <summary>
    /// Starts <c>ollama serve</c> hidden and detached (no window, std handles not redirected, so it outlives
    /// the launcher) with <c>OLLAMA_MODELS</c> set for this process only, like tools/start_ollama_server.ps1.
    /// </summary>
    public static Process Start(string rootPath)
    {
        var executable = ExecutablePath(rootPath);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        startInfo.ArgumentList.Add("serve");
        startInfo.Environment["OLLAMA_MODELS"] = ModelsDirectory(rootPath);

        return Process.Start(startInfo) ?? throw new InvalidOperationException("ollama.exe did not start.");
    }

    /// <summary>Stops a server this launcher started and did not hand over to the app. Never throws.</summary>
    public static void Stop(Process server)
    {
        try
        {
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree: true);
                server.WaitForExit(StopWait);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Debug.WriteLine($"[Launcher] stopping Ollama failed: {ex.GetType().Name}");
        }
    }
}
