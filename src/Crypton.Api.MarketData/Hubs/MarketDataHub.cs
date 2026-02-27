using MarketDataService.Models;
using Microsoft.AspNetCore.SignalR;

namespace MarketDataService.Hubs;

public interface IMarketDataClient
{
    Task OnPriceUpdate(PriceTicker ticker);
    Task OnOrderBookUpdate(OrderBook orderBook);
    Task OnTrade(Trade trade);
    Task OnBalanceUpdate(List<Balance> balances);
    Task OnConnectionStatus(bool isConnected);
}

public class MarketDataHub : Hub<IMarketDataClient>
{
    private readonly ILogger<MarketDataHub> _logger;

    public MarketDataHub(ILogger<MarketDataHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToPrices(List<string> symbols)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "prices");
        _logger.LogInformation("Client {ConnectionId} subscribed to prices: {Symbols}", Context.ConnectionId, string.Join(", ", symbols));
    }

    public async Task UnsubscribeFromPrices()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "prices");
        _logger.LogInformation("Client {ConnectionId} unsubscribed from prices", Context.ConnectionId);
    }
}
