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
        await Groups.AddToGroupAsync(Context.ConnectionId, "all_clients");
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected. Reason: {Reason}",
            Context.ConnectionId, exception?.Message ?? "unknown");
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

    public async Task SubscribeToSymbols(List<string> symbols)
    {
        foreach (var symbol in symbols)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"symbol_{symbol}");
        }
        
        _logger.LogInformation("Client {ConnectionId} subscribed to symbols: {Symbols}", 
            Context.ConnectionId, string.Join(", ", symbols));
    }

    public async Task UnsubscribeFromSymbols(List<string> symbols)
    {
        foreach (var symbol in symbols)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"symbol_{symbol}");
        }
        
        _logger.LogInformation("Client {ConnectionId} unsubscribed from symbols: {Symbols}", 
            Context.ConnectionId, string.Join(", ", symbols));
    }

    public async Task SubscribeToTrades(List<string> symbols)
    {
        foreach (var symbol in symbols)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"trades_{symbol}");
        }
        
        _logger.LogInformation("Client {ConnectionId} subscribed to trades: {Symbols}", 
            Context.ConnectionId, string.Join(", ", symbols));
    }

    public async Task UnsubscribeFromTrades(List<string> symbols)
    {
        foreach (var symbol in symbols)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trades_{symbol}");
        }
    }

    public async Task SubscribeToBalance()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "balance");
        _logger.LogInformation("Client {ConnectionId} subscribed to balance updates", Context.ConnectionId);
    }

    public async Task UnsubscribeFromBalance()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "balance");
        _logger.LogInformation("Client {ConnectionId} unsubscribed from balance updates", Context.ConnectionId);
    }
}
