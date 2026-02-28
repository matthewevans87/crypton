namespace Crypton.Api.ExecutionService.Exchange;

/// <summary>Base exception for all exchange adapter errors.</summary>
public class ExchangeAdapterException : Exception
{
    public ExchangeAdapterException(string message) : base(message) { }
    public ExchangeAdapterException(string message, Exception inner) : base(message, inner) { }
}

public sealed class AuthenticationException : ExchangeAdapterException
{
    public AuthenticationException(string message) : base(message) { }
}

public sealed class RateLimitException : ExchangeAdapterException
{
    public DateTimeOffset? ResumesAt { get; }
    public RateLimitException(string message, DateTimeOffset? resumesAt = null) : base(message)
        => ResumesAt = resumesAt;
}

public sealed class OrderNotFoundException : ExchangeAdapterException
{
    public string OrderId { get; }
    public OrderNotFoundException(string orderId) : base($"Order '{orderId}' not found.")
        => OrderId = orderId;
}
