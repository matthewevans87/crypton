using System.Text.Json;
using Crypton.Tools.Infrastructure;
using Crypton.Tools.Tools;
using Spectre.Console.Cli;
using Xunit;

namespace AgentRunner.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="ToolSchemaGenerator"/>.
/// </summary>
public class ToolSchemaGeneratorTests
{
    // ─── GenerateSchema ──────────────────────────────────────────────────────

    [Fact]
    public void GenerateSchema_ReturnsValidJson()
    {
        var json = ToolSchemaGenerator.GenerateSchema(typeof(WebSearchTool.Settings));

        Assert.NotNull(json);
        var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void GenerateSchema_RootTypeIsObject()
    {
        var json = ToolSchemaGenerator.GenerateSchema(typeof(WebSearchTool.Settings));
        var doc = JsonDocument.Parse(json);

        Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void GenerateSchema_IncludesQueryPropertyInProperties()
    {
        var json = ToolSchemaGenerator.GenerateSchema(typeof(WebSearchTool.Settings));
        var doc = JsonDocument.Parse(json);

        var props = doc.RootElement.GetProperty("properties");
        Assert.True(props.TryGetProperty("--query", out _),
            "Expected '--query' in properties");
    }

    [Fact]
    public void GenerateSchema_StringPropertiesAreNotInRequiredArray()
    {
        // String properties are reference types; per schema generator convention they
        // are treated as optional and NOT placed in the "required" array.
        var json = ToolSchemaGenerator.GenerateSchema(typeof(WebSearchTool.Settings));
        var doc = JsonDocument.Parse(json);

        // No "required" field at all for WebSearchTool.Settings (all props are optional types)
        Assert.False(doc.RootElement.TryGetProperty("required", out _),
            "String reference-type properties should not produce a 'required' array");
    }

    [Fact]
    public void GenerateSchema_IncludesDescriptionOnProperty()
    {
        var json = ToolSchemaGenerator.GenerateSchema(typeof(WebSearchTool.Settings));
        var doc = JsonDocument.Parse(json);

        var queryProp = doc.RootElement.GetProperty("properties").GetProperty("--query");
        var desc = queryProp.GetProperty("description").GetString();

        Assert.False(string.IsNullOrWhiteSpace(desc));
    }

    [Fact]
    public void GenerateSchema_IncludesDefaultValueOnOptionalProperty()
    {
        var json = ToolSchemaGenerator.GenerateSchema(typeof(WebSearchTool.Settings));
        var doc = JsonDocument.Parse(json);

        var countProp = doc.RootElement.GetProperty("properties").GetProperty("--count");
        Assert.True(countProp.TryGetProperty("default", out var defaultVal));
        Assert.Equal(10, defaultVal.GetInt32());
    }

    [Fact]
    public void GenerateSchema_NoOptionsType_ReturnsEmptyObjectSchema()
    {
        var json = ToolSchemaGenerator.GenerateSchema(typeof(EmptySettings));
        var doc = JsonDocument.Parse(json);

        Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("properties").EnumerateObject().Count());
        Assert.False(doc.RootElement.TryGetProperty("required", out _));
    }

    // ─── JsonToArgs ──────────────────────────────────────────────────────────

    [Fact]
    public void JsonToArgs_SingleStringProperty_ReturnsOptionValuePair()
    {
        var args = ToolSchemaGenerator.JsonToArgs(
            typeof(WebSearchTool.Settings), "{\"query\": \"bitcoin\"}");

        Assert.Equal(new[] { "--query", "bitcoin" }, args);
    }

    [Fact]
    public void JsonToArgs_MultipleProperties_ReturnsAllPairs()
    {
        var args = ToolSchemaGenerator.JsonToArgs(
            typeof(WebSearchTool.Settings), "{\"query\": \"eth\", \"count\": 5}");

        Assert.Contains("--query", args);
        Assert.Contains("eth", args);
        Assert.Contains("--count", args);
        Assert.Contains("5", args);
    }

    [Fact]
    public void JsonToArgs_ArgsEnvelope_Unwraps()
    {
        var args = ToolSchemaGenerator.JsonToArgs(
            typeof(WebSearchTool.Settings), "{\"args\": {\"query\": \"sol\"}}");

        Assert.Equal(new[] { "--query", "sol" }, args);
    }

    [Fact]
    public void JsonToArgs_EmptyJson_ReturnsEmpty()
    {
        var args = ToolSchemaGenerator.JsonToArgs(typeof(WebSearchTool.Settings), "{}");
        Assert.Empty(args);
    }

    [Fact]
    public void JsonToArgs_InvalidJson_ReturnsEmpty()
    {
        var args = ToolSchemaGenerator.JsonToArgs(typeof(WebSearchTool.Settings), "not json");
        Assert.Empty(args);
    }

    [Fact]
    public void JsonToArgs_OptionLongNameAsKey_MapsCorrectly()
    {
        // Key uses the property name (lowercase) — the option long name without dashes
        var args = ToolSchemaGenerator.JsonToArgs(
            typeof(WebSearchTool.Settings), "{\"query\": \"sol\"}");

        Assert.Contains("--query", args);
        Assert.Contains("sol", args);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private sealed class EmptySettings : CommandSettings { }
}
