using AgentRunner.Agents;
using Xunit;

namespace AgentRunner.Tests.Agents;

public class AgentInvokerCompactJsonTests
{
    // ── Compact indented JSON ─────────────────────────────────────────────────

    [Fact]
    public void CompactJson_IndentedObject_StripsWhitespace()
    {
        var indented = """
            {
                "symbol": "BTC/USD",
                "rsi": 47.86,
                "signal": "neutral"
            }
            """;

        var result = AgentInvoker.CompactJson(indented);

        Assert.Equal("""{"symbol":"BTC/USD","rsi":47.86,"signal":"neutral"}""", result);
    }

    [Fact]
    public void CompactJson_IndentedArray_StripsWhitespace()
    {
        var indented = """
            [
                { "id": "1", "text": "hello" },
                { "id": "2", "text": "world" }
            ]
            """;

        var result = AgentInvoker.CompactJson(indented);

        Assert.DoesNotContain("\n", result);
        Assert.DoesNotContain("  ", result);
        Assert.StartsWith("[", result);
    }

    [Fact]
    public void CompactJson_AlreadyCompact_IsUnchanged()
    {
        var compact = """{"symbol":"BTC/USD","rsi":47.86}""";

        var result = AgentInvoker.CompactJson(compact);

        Assert.Equal(compact, result);
    }

    // ── Non-JSON passthrough ──────────────────────────────────────────────────

    [Fact]
    public void CompactJson_PlainString_ReturnedAsIs()
    {
        var plain = "No results returned (possible auth token expiry)";

        var result = AgentInvoker.CompactJson(plain);

        Assert.Equal(plain, result);
    }

    [Fact]
    public void CompactJson_InvalidJson_ReturnedAsIs()
    {
        var invalid = "{ not valid json ]";

        var result = AgentInvoker.CompactJson(invalid);

        Assert.Equal(invalid, result);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void CompactJson_EmptyString_ReturnedAsIs()
    {
        Assert.Equal("", AgentInvoker.CompactJson(""));
    }

    [Fact]
    public void CompactJson_NullEquivalentWhitespace_ReturnedAsIs()
    {
        Assert.Equal("   ", AgentInvoker.CompactJson("   "));
    }

    [Fact]
    public void CompactJson_JsonStringValue_ReturnedCompact()
    {
        // A JSON-encoded string (e.g. a tool that returns a plain quoted string)
        var jsonString = "\"hello world\"";

        var result = AgentInvoker.CompactJson(jsonString);

        Assert.Equal("\"hello world\"", result);
    }

    [Fact]
    public void CompactJson_NestedObject_StripsAllWhitespace()
    {
        var nested = """
            {
                "outer": {
                    "inner": [1, 2, 3],
                    "flag": true
                }
            }
            """;

        var result = AgentInvoker.CompactJson(nested);

        Assert.Equal("""{"outer":{"inner":[1,2,3],"flag":true}}""", result);
    }
}
