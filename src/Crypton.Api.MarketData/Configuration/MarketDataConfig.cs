namespace MarketDataService.Configuration;

public sealed class MarketDataConfig
{
    public ExchangeConfig Exchange { get; init; } = new();
    public KrakenConfig Kraken { get; init; } = new();
}

public sealed class ExchangeConfig
{
    public bool UseMock { get; init; } = false;
}

public sealed class KrakenConfig
{
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    public string WsBaseUrl { get; init; } = "wss://ws.kraken.com/v2";
    public string RestBaseUrl { get; init; } = "https://api.kraken.com";
}
