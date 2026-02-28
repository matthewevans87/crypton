using System.Net;
using System.Text;
using System.Text.Json;
using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Exchange;

/// <summary>
/// Allows tests to control HTTP responses without a real network.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => _handler(request);
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

file static class KrakenResponseBuilder
{
    public static HttpResponseMessage Ok(object result) =>
        Json(new { error = Array.Empty<string>(), result });

    public static HttpResponseMessage KrakenError(string errorCode) =>
        Json(new { error = new[] { errorCode }, result = new { } });

    private static HttpResponseMessage Json(object body)
    {
        var json = JsonSerializer.Serialize(body);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage Unauthorized() =>
        new(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":[\"EGeneral:Permission denied\"]}", Encoding.UTF8, "application/json")
        };
}

file static class AdapterFactory
{
    public static KrakenRestAdapter Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var httpHandler = new MockHttpMessageHandler(handler);
        var httpClient = new HttpClient(httpHandler) { BaseAddress = new Uri("https://api.kraken.com") };
        // Use a valid base64 string as the secret so HMAC-SHA512 does not throw.
        return new KrakenRestAdapter("test-key", Convert.ToBase64String(new byte[32]), httpClient, NullLogger<KrakenRestAdapter>.Instance);
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class KrakenRestAdapterTests
{
    // -----------------------------------------------------------------------
    // PlaceOrderAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlaceOrderAsync_BuyMarket_ReturnsAcknowledgement()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.Ok(new
            {
                txids = new[] { "SAMPLE-ORDER-1" },
                descr = new { order = "buy 0.01 XBTUSD @ market" }
            })));

        var request = new PlaceOrderRequest
        {
            InternalId = "int-1",
            Asset = "XBT/USD",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0.01m
        };

        var ack = await adapter.PlaceOrderAsync(request);

        ack.ExchangeOrderId.Should().Be("SAMPLE-ORDER-1");
        ack.InternalId.Should().Be("int-1");
    }

    [Fact]
    public async Task PlaceOrderAsync_KrakenError_ThrowsExchangeAdapterException()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.KrakenError("EOrder:Insufficient funds")));

        var request = new PlaceOrderRequest
        {
            InternalId = "int-2",
            Asset = "XBT/USD",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 100m
        };

        var act = () => adapter.PlaceOrderAsync(request);

        await act.Should().ThrowAsync<ExchangeAdapterException>()
            .WithMessage("*Insufficient funds*");
    }

    [Fact]
    public async Task PlaceOrderAsync_RateLimitError_ThrowsRateLimitException()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.KrakenError("EOrder:Rate limit exceeded")));

        var request = new PlaceOrderRequest
        {
            InternalId = "int-3",
            Asset = "XBT/USD",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0.01m
        };

        var act = () => adapter.PlaceOrderAsync(request);

        await act.Should().ThrowAsync<RateLimitException>();
    }

    [Fact]
    public async Task PlaceOrderAsync_RateLimitError_SetsIsRateLimited()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.KrakenError("EOrder:Rate limit exceeded")));

        var request = new PlaceOrderRequest
        {
            InternalId = "int-4",
            Asset = "XBT/USD",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0.01m
        };

        try { await adapter.PlaceOrderAsync(request); } catch (RateLimitException) { }

        adapter.IsRateLimited.Should().BeTrue();
        adapter.RateLimitResumesAt.Should().NotBeNull();
        adapter.RateLimitResumesAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    // -----------------------------------------------------------------------
    // CancelOrderAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CancelOrderAsync_Success_ReturnsTrue()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.Ok(new { count = 1 })));

        var result = await adapter.CancelOrderAsync("ORDER-XYZ");

        result.Success.Should().BeTrue();
        result.ExchangeOrderId.Should().Be("ORDER-XYZ");
    }

    [Fact]
    public async Task CancelOrderAsync_Failure_ReturnsFalse()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.KrakenError("EOrder:Unknown order")));

        var result = await adapter.CancelOrderAsync("UNKNOWN-ORDER");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown order");
    }

    // -----------------------------------------------------------------------
    // GetOrderStatusAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetOrderStatusAsync_Open_ReturnsOpenStatus()
    {
        const string orderId = "OPEN-1";
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.Ok(new Dictionary<string, object>
            {
                [orderId] = new { status = "open", vol_exec = "0.0", price = "0" }
            })));

        var status = await adapter.GetOrderStatusAsync(orderId);

        status.Status.Should().Be(OrderStatus.Open);
        status.ExchangeOrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task GetOrderStatusAsync_Closed_ReturnsFilledStatus()
    {
        const string orderId = "CLOSED-1";
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.Ok(new Dictionary<string, object>
            {
                [orderId] = new { status = "closed", vol_exec = "0.5", price = "50000" }
            })));

        var status = await adapter.GetOrderStatusAsync(orderId);

        status.Status.Should().Be(OrderStatus.Filled);
        status.FilledQuantity.Should().Be(0.5m);
        status.AverageFillPrice.Should().Be(50000m);
    }

    [Fact]
    public async Task GetOrderStatusAsync_NotFound_ThrowsOrderNotFoundException()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.Ok(new { })));

        var act = () => adapter.GetOrderStatusAsync("MISSING-ORDER");

        await act.Should().ThrowAsync<OrderNotFoundException>()
            .Where(e => e.OrderId == "MISSING-ORDER");
    }

    // -----------------------------------------------------------------------
    // GetAccountBalanceAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAccountBalanceAsync_ReturnsBalance()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.Ok(new { ZUSD = "5000.00", XXBT = "0.5" })));

        var balance = await adapter.GetAccountBalanceAsync();

        balance.AvailableUsd.Should().Be(5000m);
        balance.AssetBalances.Should().ContainKey("XXBT");
        balance.AssetBalances["XXBT"].Should().Be(0.5m);
    }

    // -----------------------------------------------------------------------
    // Authentication errors
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Http401_ThrowsAuthenticationException()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.Unauthorized()));

        var act = () => adapter.GetAccountBalanceAsync();

        await act.Should().ThrowAsync<AuthenticationException>()
            .WithMessage("*401*");
    }

    [Fact]
    public async Task KrakenApiInvalidKey_ThrowsAuthenticationException()
    {
        var adapter = AdapterFactory.Create(_ => Task.FromResult(
            KrakenResponseBuilder.KrakenError("EAPI:Invalid key")));

        var act = () => adapter.GetAccountBalanceAsync();

        await act.Should().ThrowAsync<AuthenticationException>();
    }
}
