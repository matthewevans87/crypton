using System.Diagnostics;
using System.Net;
using AgentRunner.Configuration;

namespace AgentRunner.Startup;

/// <summary>
/// Checks that every external dependency the Agent Runner needs is reachable and healthy
/// before the loop starts. All checks run in parallel; each failure produces an independent
/// error message.
/// </summary>
public class StartupValidator
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;

    // Injected in tests to avoid spawning a real process.
    // Returns null on success, or an error string on failure.
    private readonly Func<string, CancellationToken, Task<string?>>? _cliChecker;

    public StartupValidator(
        HttpClient httpClient,
        Func<string, CancellationToken, Task<string?>>? cliChecker = null)
    {
        _httpClient = httpClient;
        _cliChecker = cliChecker;
    }

    public async Task<StartupValidationResult> ValidateAsync(
        AgentRunnerConfig config,
        CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(
            CheckOllamaAsync(config.Ollama.BaseUrl, cancellationToken),
            CheckHttpServiceAsync("Execution Service", config.Tools.ExecutionService.BaseUrl, "/health/live", cancellationToken),
            CheckHttpServiceAsync("Market Data Service", config.Tools.MarketDataService.BaseUrl, "/health/live", cancellationToken),
            CheckBraveSearchAsync(config.Tools.BraveSearch.ApiKey, cancellationToken),
            CheckBirdCliAsync(cancellationToken));

        var errors = results.Where(e => e != null).Select(e => e!).ToList();
        return new StartupValidationResult(errors.Count == 0, errors);
    }

    private async Task<string?> CheckOllamaAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/tags";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CheckTimeout);
            var response = await _httpClient.GetAsync(url, cts.Token);
            return response.IsSuccessStatusCode
                ? null
                : $"Ollama is not available at {baseUrl}: HTTP {(int)response.StatusCode}";
        }
        catch (OperationCanceledException)
        {
            return $"Ollama did not respond within {CheckTimeout.TotalSeconds}s at {baseUrl}";
        }
        catch (Exception ex)
        {
            return $"Ollama is not reachable at {baseUrl}: {ex.Message}";
        }
    }

    private async Task<string?> CheckHttpServiceAsync(
        string name, string baseUrl, string path, CancellationToken cancellationToken)
    {
        var url = $"{baseUrl.TrimEnd('/')}{path}";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CheckTimeout);
            var response = await _httpClient.GetAsync(url, cts.Token);
            return response.IsSuccessStatusCode
                ? null
                : $"{name} is not healthy at {baseUrl}: HTTP {(int)response.StatusCode}";
        }
        catch (OperationCanceledException)
        {
            return $"{name} did not respond within {CheckTimeout.TotalSeconds}s at {baseUrl}";
        }
        catch (Exception ex)
        {
            return $"{name} is not reachable at {baseUrl}: {ex.Message}";
        }
    }

    private async Task<string?> CheckBraveSearchAsync(string apiKey, CancellationToken cancellationToken)
    {
        const string url = "https://api.search.brave.com/res/v1/web/search?q=test&count=1";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CheckTimeout);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Subscription-Token", apiKey);
            var response = await _httpClient.SendAsync(request, cts.Token);
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Brave Search API key is invalid (HTTP 401 Unauthorized)",
                HttpStatusCode.Forbidden => "Brave Search API key is forbidden or quota exceeded (HTTP 403 Forbidden)",
                _ => null
            };
        }
        catch (OperationCanceledException)
        {
            return $"Brave Search API did not respond within {CheckTimeout.TotalSeconds}s";
        }
        catch (Exception ex)
        {
            return $"Brave Search API is not reachable: {ex.Message}";
        }
    }

    private async Task<string?> CheckBirdCliAsync(CancellationToken cancellationToken)
    {
        var checker = _cliChecker ?? DefaultCheckBirdCliAsync;
        return await checker("bird", cancellationToken);
    }

    private static async Task<string?> DefaultCheckBirdCliAsync(string executable, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CheckTimeout);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "whoami",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderr = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                var error = await stderr;
                var detail = string.IsNullOrWhiteSpace(error) ? $"exit code {process.ExitCode}" : error.Trim();
                return $"'{executable}' is not authenticated: {detail}";
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return $"'{executable}' CLI did not respond within {CheckTimeout.TotalSeconds}s";
        }
        catch (Exception ex)
        {
            return $"'{executable}' CLI is not available: {ex.Message}";
        }
    }
}

public record StartupValidationResult(bool IsValid, IReadOnlyList<string> Errors);
