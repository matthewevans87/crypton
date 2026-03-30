using AgentRunner.Abstractions;
using Crypton.Tools;
using Crypton.Tools.Tools;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class ToolDocumentationTests
{
    // ─── ToMarkdown ───────────────────────────────────────────────────────────

    [Fact]
    public void ToMarkdown_IncludesToolNameAsHeading()
    {
        var doc = BuildMinimalDoc();
        var md = doc.ToMarkdown("my_tool");
        Assert.Contains("## `my_tool`", md);
    }

    [Fact]
    public void ToMarkdown_IncludesLongDescription()
    {
        var doc = BuildMinimalDoc();
        var md = doc.ToMarkdown("t");
        Assert.Contains("A detailed description.", md);
    }

    [Fact]
    public void ToMarkdown_IncludesReturnsSection()
    {
        var doc = BuildMinimalDoc();
        var md = doc.ToMarkdown("t");
        Assert.Contains("### Returns", md);
        Assert.Contains("Return value text.", md);
    }

    [Fact]
    public void ToMarkdown_WhenHasParameters_IncludesParameterTable()
    {
        var doc = new ToolDocumentation(
            LongDescription: "desc",
            Parameters:
            [
                new("query",  "string",  Required: true,  Default: null,    "The query."),
                new("count",  "integer", Required: false, Default: "10",    "Number of results."),
            ],
            Returns: "Some results.",
            Examples: [],
            Notes: []);

        var md = doc.ToMarkdown("t");

        Assert.Contains("### Parameters", md);
        Assert.Contains("| `query` |", md);
        Assert.Contains("| yes |", md);
        Assert.Contains("| `count` |", md);
        Assert.Contains("| no |", md);
        Assert.Contains("| `10` |", md);
        Assert.Contains("| \u2014 |", md);   // null default renders as em-dash
    }

    [Fact]
    public void ToMarkdown_WhenNoParameters_OmitsParameterSection()
    {
        var doc = new ToolDocumentation(
            LongDescription: "desc",
            Parameters: [],
            Returns: "r",
            Examples: [],
            Notes: []);

        var md = doc.ToMarkdown("t");
        Assert.DoesNotContain("### Parameters", md);
    }

    [Fact]
    public void ToMarkdown_WhenHasExamples_IncludesJsonCodeBlock()
    {
        var doc = new ToolDocumentation(
            LongDescription: "d",
            Parameters: [],
            Returns: "r",
            Examples: ["{\"query\": \"bitcoin\"}"],
            Notes: []);

        var md = doc.ToMarkdown("t");
        Assert.Contains("### Example", md);
        Assert.Contains("```json", md);
        Assert.Contains("{\"query\": \"bitcoin\"}", md);
    }

    [Fact]
    public void ToMarkdown_WhenNoExamples_OmitsExampleSection()
    {
        var doc = new ToolDocumentation(
            LongDescription: "d",
            Parameters: [],
            Returns: "r",
            Examples: [],
            Notes: []);

        var md = doc.ToMarkdown("t");
        Assert.DoesNotContain("### Example", md);
    }

    [Fact]
    public void ToMarkdown_WhenHasNotes_IncludesBulletedNotes()
    {
        var doc = new ToolDocumentation(
            LongDescription: "d",
            Parameters: [],
            Returns: "r",
            Examples: [],
            Notes: ["First note.", "Second note."]);

        var md = doc.ToMarkdown("t");
        Assert.Contains("### Notes", md);
        Assert.Contains("- First note.", md);
        Assert.Contains("- Second note.", md);
    }

    [Fact]
    public void ToMarkdown_WhenNoNotes_OmitsNotesSection()
    {
        var doc = BuildMinimalDoc();
        var md = doc.ToMarkdown("t");
        Assert.DoesNotContain("### Notes", md);
    }

    // ─── Tool class Documentation spot-checks ─────────────────────────────────

    [Fact]
    public void GetPriceTool_Documentation_NotesContainGetPricePreferenceHint()
    {
        var tool = GetPriceToolFactory.Create();
        var md = tool.Documentation.ToMarkdown(tool.Name);
        Assert.Contains("get_price", md);
        Assert.Matches("(?i)instead of.*web_search|web_search.*instead", md);
    }

    [Fact]
    public void WebSearchTool_Documentation_ToMarkdownContainsToolName()
    {
        var tool = WebSearchToolFactory.Create();
        var md = tool.Documentation.ToMarkdown(tool.Name);
        Assert.Contains("## `web_search`", md);
    }

    [Fact]
    public void AllToolClasses_HaveNonNullDocumentation()
    {
        IAgentTool[] tools =
        [
            WebSearchToolFactory.Create(),
            WebFetchToolFactory.Create(),
            GetPriceToolFactory.Create(),
            MacroSignalsToolFactory.Create(),
            OrderBookToolFactory.Create(),
            TechnicalIndicatorsToolFactory.Create(),
        ];

        foreach (var tool in tools)
        {
            Assert.NotNull(tool.Documentation);
            Assert.False(string.IsNullOrWhiteSpace(tool.Documentation.LongDescription),
                $"{tool.Name} LongDescription must be set");
            Assert.False(string.IsNullOrWhiteSpace(tool.Documentation.Returns),
                $"{tool.Name} Returns must be set");
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static ToolDocumentation BuildMinimalDoc() => new(
        LongDescription: "A detailed description.",
        Parameters: [],
        Returns: "Return value text.",
        Examples: [],
        Notes: []);
}

// Minimal factory helpers so tests don't depend on DI or live HTTP.
file static class GetPriceToolFactory
{
    public static GetPriceTool Create() =>
        new(new HttpClient(), "http://localhost", new NoOpToolExecutor());
}

file static class WebSearchToolFactory
{
    public static WebSearchTool Create() =>
        new(new HttpClient(), "fake-api-key", new NoOpToolExecutor());
}

file static class WebFetchToolFactory
{
    public static WebFetchTool Create() =>
        new(new HttpClient(), new NoOpToolExecutor());
}

file static class MacroSignalsToolFactory
{
    public static MacroSignalsTool Create() =>
        new(new HttpClient(), "http://localhost", new NoOpToolExecutor());
}

file static class OrderBookToolFactory
{
    public static OrderBookTool Create() =>
        new(new HttpClient(), "http://localhost", new NoOpToolExecutor());
}

file static class TechnicalIndicatorsToolFactory
{
    public static TechnicalIndicatorsTool Create() =>
        new(new HttpClient(), "http://localhost", new NoOpToolExecutor());
}

file sealed class NoOpToolExecutor : Crypton.Tools.IToolExecutor
{
    public Task<T> ExecuteWithResilienceAsync<T>(string toolName, Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        => operation(ct);
}
