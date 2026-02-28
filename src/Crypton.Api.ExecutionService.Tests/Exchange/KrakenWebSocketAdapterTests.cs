using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Exchange;

public sealed class KrakenWebSocketAdapterTests
{
    private static KrakenWebSocketAdapter CreateAdapter() =>
        new(
            wsBaseUrl: "wss://ws.kraken.com/v2",
            maxReconnectAttempts: 5,
            reconnectDelaySeconds: 2,
            logger: NullLogger<KrakenWebSocketAdapter>.Instance);

    // -----------------------------------------------------------------------
    // Message parsing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ParseTickerMessage_ValidSnapshot_RaisesCallback()
    {
        var adapter = CreateAdapter();
        var snapshots = new List<MarketSnapshot>();

        const string json = """
            {
              "channel": "ticker",
              "type": "snapshot",
              "data": [{
                "symbol": "BTC/USD",
                "bid": 50000.00,
                "ask": 50010.00,
                "last": 50005.00,
                "timestamp": "2024-01-01T00:00:00.000Z"
              }]
            }
            """;

        await adapter.ProcessMessageAsync(json, s => { snapshots.Add(s); return Task.CompletedTask; });

        snapshots.Should().HaveCount(1);
        snapshots[0].Asset.Should().Be("BTC/USD");
        snapshots[0].Bid.Should().Be(50000m);
        snapshots[0].Ask.Should().Be(50010m);
        snapshots[0].Timestamp.Should().Be(DateTimeOffset.Parse("2024-01-01T00:00:00.000Z"));
    }

    [Fact]
    public async Task ParseTickerMessage_UpdateType_AlsoRaisesCallback()
    {
        var adapter = CreateAdapter();
        var snapshots = new List<MarketSnapshot>();

        const string json = """
            {
              "channel": "ticker",
              "type": "update",
              "data": [{
                "symbol": "ETH/USD",
                "bid": 3000.00,
                "ask": 3001.00,
                "last": 3000.50,
                "timestamp": "2024-06-01T12:00:00.000Z"
              }]
            }
            """;

        await adapter.ProcessMessageAsync(json, s => { snapshots.Add(s); return Task.CompletedTask; });

        snapshots.Should().HaveCount(1);
        snapshots[0].Asset.Should().Be("ETH/USD");
        snapshots[0].Bid.Should().Be(3000m);
    }

    [Fact]
    public async Task ParseTickerMessage_OtherChannel_IsIgnored()
    {
        var adapter = CreateAdapter();
        var snapshots = new List<MarketSnapshot>();

        const string json = """
            {
              "channel": "heartbeat",
              "data": []
            }
            """;

        await adapter.ProcessMessageAsync(json, s => { snapshots.Add(s); return Task.CompletedTask; });

        snapshots.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Rate limiting
    // -----------------------------------------------------------------------

    [Fact]
    public void IsRateLimited_AlwaysFalse()
    {
        var adapter = CreateAdapter();

        adapter.IsRateLimited.Should().BeFalse();
        adapter.RateLimitResumesAt.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Unsupported methods
    // -----------------------------------------------------------------------

    [Fact]
    public void PlaceOrderAsync_ThrowsNotSupportedException()
    {
        var adapter = CreateAdapter();

        var request = new PlaceOrderRequest
        {
            InternalId = "test",
            Asset = "BTC/USD",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        };

        Action act = () => adapter.PlaceOrderAsync(request);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*WebSocket adapter is for market data only*");
    }
}
