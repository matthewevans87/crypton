using System.Net;
using AgentRunner.Configuration;
using AgentRunner.Startup;
using Xunit;

namespace AgentRunner.Tests.Startup;

public class StartupValidatorTests
{
    private static AgentRunnerConfig CreateConfig() => new()
    {
        Ollama = new OllamaConfig { BaseUrl = "http://localhost:11434" },
        Tools = new ToolConfig
        {
            BraveSearch = new BraveSearchConfig { ApiKey = "test-key" },
            Bird = new BirdConfig { BaseUrl = "http://localhost:11435" },
            ExecutionService = new ExecutionServiceConfig { BaseUrl = "http://localhost:5000" },
            MarketDataService = new MarketDataServiceConfig { BaseUrl = "http://localhost:5002" }
        }
    };

    private static FakeHttpMessageHandler AllHttpOk()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Setup("http://localhost:11434/api/tags", HttpStatusCode.OK);
        handler.Setup("http://localhost:5000/health/live", HttpStatusCode.OK);
        handler.Setup("http://localhost:5002/health/live", HttpStatusCode.OK);
        handler.Setup("http://localhost:11435/health", HttpStatusCode.OK);
        handler.SetupPrefix("https://api.search.brave.com/", HttpStatusCode.OK);
        return handler;
    }

    // ── All-green ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_AllServicesAvailable_ReturnsValid()
    {
        var validator = new StartupValidator(new HttpClient(AllHttpOk()));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ── HTTP service checks ────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_OllamaUnreachable_ReturnsError()
    {
        var handler = AllHttpOk();
        handler.SetupException("http://localhost:11434/api/tags", new HttpRequestException("Connection refused"));

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Ollama", error);
    }

    [Fact]
    public async Task ValidateAsync_OllamaReturnsNonSuccess_ReturnsError()
    {
        var handler = AllHttpOk();
        handler.Setup("http://localhost:11434/api/tags", HttpStatusCode.ServiceUnavailable);

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Ollama", error);
    }

    [Fact]
    public async Task ValidateAsync_ExecutionServiceUnreachable_ReturnsError()
    {
        var handler = AllHttpOk();
        handler.SetupException("http://localhost:5000/health/live", new HttpRequestException("Connection refused"));

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Execution Service", error);
    }

    [Fact]
    public async Task ValidateAsync_MarketDataServiceUnhealthy_ReturnsError()
    {
        var handler = AllHttpOk();
        handler.Setup("http://localhost:5002/health/live", HttpStatusCode.InternalServerError);

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Market Data Service", error);
    }

    [Fact]
    public async Task ValidateAsync_BraveSearchUnauthorized_ReturnsError()
    {
        var handler = AllHttpOk();
        handler.SetupPrefix("https://api.search.brave.com/", HttpStatusCode.Unauthorized);

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Brave Search", error);
    }

    [Fact]
    public async Task ValidateAsync_BraveSearchForbidden_ReturnsError()
    {
        var handler = AllHttpOk();
        handler.SetupPrefix("https://api.search.brave.com/", HttpStatusCode.Forbidden);

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Brave Search", error);
    }

    [Fact]
    public async Task ValidateAsync_BraveSearchBadRequest_IsNotAnError()
    {
        // 400 means the key was accepted but the probe query was malformed — that's fine.
        var handler = AllHttpOk();
        handler.SetupPrefix("https://api.search.brave.com/", HttpStatusCode.BadRequest);

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.True(result.IsValid);
    }

    // ── Bird Server check ──────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_BirdServerAvailable_ReturnsValid()
    {
        var validator = new StartupValidator(new HttpClient(AllHttpOk()));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_BirdServerUnreachable_ReturnsError()
    {
        var handler = AllHttpOk();
        handler.SetupException("http://localhost:11435/health", new HttpRequestException("Connection refused"));

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Bird Server", error);
    }

    [Fact]
    public async Task ValidateAsync_BirdServerUnhealthy_ReturnsError()
    {
        var handler = AllHttpOk();
        handler.Setup("http://localhost:11435/health", HttpStatusCode.InternalServerError);

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Bird Server", error);
    }

    // ── Multiple failures ──────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_MultipleServicesUnavailable_ReturnsAllErrors()
    {
        var handler = AllHttpOk();
        handler.SetupException("http://localhost:11434/api/tags", new HttpRequestException("Connection refused"));
        handler.SetupException("http://localhost:5000/health/live", new HttpRequestException("Connection refused"));
        handler.SetupException("http://localhost:11435/health", new HttpRequestException("Connection refused"));
        handler.SetupPrefix("https://api.search.brave.com/", HttpStatusCode.Unauthorized);

        var validator = new StartupValidator(new HttpClient(handler));
        var result = await validator.ValidateAsync(CreateConfig());

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("Ollama"));
        Assert.Contains(result.Errors, e => e.Contains("Execution Service"));
        Assert.Contains(result.Errors, e => e.Contains("Brave Search"));
        Assert.Contains(result.Errors, e => e.Contains("Bird Server"));
    }
}

/// <summary>
/// Minimal HTTP handler for unit tests. Supports exact-URL and prefix-based response setup.
/// SetupException and Setup for the same URL: exception takes priority.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, HttpStatusCode> _exact = new(StringComparer.Ordinal);
    private readonly List<(string Prefix, HttpStatusCode Status)> _prefixes = [];
    private readonly Dictionary<string, Exception> _exceptions = new(StringComparer.Ordinal);

    public void Setup(string url, HttpStatusCode statusCode) => _exact[url] = statusCode;

    public void SetupPrefix(string urlPrefix, HttpStatusCode statusCode) =>
        _prefixes.Insert(0, (urlPrefix, statusCode)); // prepend so later calls override earlier

    public void SetupException(string url, Exception exception) => _exceptions[url] = exception;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";

        if (_exceptions.TryGetValue(url, out var ex))
            throw ex;

        if (_exact.TryGetValue(url, out var exactStatus))
            return Task.FromResult(new HttpResponseMessage(exactStatus));

        foreach (var (prefix, status) in _prefixes)
        {
            if (url.StartsWith(prefix, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(status));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
