using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRunner.Domain;

namespace AgentRunner.Orchestration;

/// <summary>
/// Validates agent output artifacts before they are saved. Pure static methods — no DI.
/// </summary>
public static class ArtifactValidator
{
    public static ValidationResult Validate(LoopState state, string content) => state switch
    {
        LoopState.Plan => ValidatePlan(content),
        LoopState.Research => ValidateResearch(content),
        LoopState.Analyze => ValidateAnalysis(content),
        LoopState.Synthesize => ValidateStrategy(content),
        LoopState.Evaluate => ValidateEvaluation(content),
        _ => ValidationResult.Success()
    };

    private static ValidationResult ValidatePlan(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ValidationResult.Failure("Plan content is empty");

        string[] required = [
            "## 1. Meta-Signals",
            "## 2. Macro Market Conditions",
            "## 3. Technical Signals",
            "## 4. On-Chain Signals",
            "## 5. News & Social Signals",
            "## 6. Research Agenda",
            "## 7. Signals Deprioritized"
        ];

        var errors = required
            .Where(s => !content.Contains(s))
            .Select(s => $"Missing required section: {s}")
            .ToList();

        return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
    }

    private static ValidationResult ValidateResearch(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ValidationResult.Failure("Research content is empty");

        if (!content.Contains("## Investigation Findings", StringComparison.OrdinalIgnoreCase))
            return ValidationResult.Failure("Missing Investigation Findings section");

        return ValidationResult.Success();
    }

    private static ValidationResult ValidateAnalysis(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ValidationResult.Failure("Analysis content is empty");

        var errors = new List<string>();
        if (!content.Contains("## Market Overview", StringComparison.OrdinalIgnoreCase))
            errors.Add("Missing Market Overview section");
        if (!content.Contains("## Per-Asset Analysis", StringComparison.OrdinalIgnoreCase))
            errors.Add("Missing Per-Asset Analysis section");

        return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
    }

    private static ValidationResult ValidateStrategy(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ValidationResult.Failure("Strategy content is empty");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(content); }
        catch (JsonException ex) { return ValidationResult.Failure($"Invalid JSON: {ex.Message}"); }
        using (doc) { }

        var errors = new List<string>();

        try
        {
            var strategy = JsonSerializer.Deserialize<StrategySchema>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (string.IsNullOrEmpty(strategy?.Mode))
                errors.Add("Missing required field: mode");
            else if (strategy.Mode != "paper" && strategy.Mode != "live")
                errors.Add($"Invalid mode: {strategy.Mode}. Must be 'paper' or 'live'");

            if (string.IsNullOrEmpty(strategy?.ValidityWindow))
                errors.Add("Missing required field: validityWindow");

            if (string.IsNullOrEmpty(strategy?.Posture))
                errors.Add("Missing required field: posture");

            if (strategy?.PortfolioRisk is null)
            {
                errors.Add("Missing required field: portfolioRisk");
            }
            else
            {
                if (strategy.PortfolioRisk.MaxDrawdownPct is < 0 or > 1)
                    errors.Add("portfolioRisk.maxDrawdownPct must be between 0 and 1");
                if (strategy.PortfolioRisk.DailyLossLimitUsd < 0)
                    errors.Add("portfolioRisk.dailyLossLimitUsd must be non-negative");
                if (strategy.PortfolioRisk.MaxTotalExposurePct is < 0 or > 1)
                    errors.Add("portfolioRisk.maxTotalExposurePct must be between 0 and 1");
                if (strategy.PortfolioRisk.MaxPerPositionPct is < 0 or > 1)
                    errors.Add("portfolioRisk.maxPerPositionPct must be between 0 and 1");
            }

            if (strategy?.Positions is null)
                errors.Add("Missing required field: positions");
        }
        catch (Exception ex)
        {
            errors.Add($"Schema validation error: {ex.Message}");
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
    }

    private static ValidationResult ValidateEvaluation(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ValidationResult.Failure("Evaluation content is empty");

        if (!content.Contains("## Performance Metrics", StringComparison.OrdinalIgnoreCase))
            return ValidationResult.Failure("Missing Performance Metrics section");

        return ValidationResult.Success();
    }

    // ─── Schema projection for strategy validation only ────────────────────────

    private sealed class StrategySchema
    {
        [JsonPropertyName("mode")] public string? Mode { get; init; }
        [JsonPropertyName("validity_window")] public string? ValidityWindow { get; init; }
        [JsonPropertyName("posture")] public string? Posture { get; init; }
        [JsonPropertyName("portfolio_risk")] public PortfolioRiskSchema? PortfolioRisk { get; init; }
        [JsonPropertyName("positions")] public List<PositionSchema>? Positions { get; init; }
    }

    private sealed class PortfolioRiskSchema
    {
        [JsonPropertyName("max_drawdown_pct")] public double MaxDrawdownPct { get; init; }
        [JsonPropertyName("daily_loss_limit_usd")] public double DailyLossLimitUsd { get; init; }
        [JsonPropertyName("max_total_exposure_pct")] public double MaxTotalExposurePct { get; init; }
        [JsonPropertyName("max_per_position_pct")] public double MaxPerPositionPct { get; init; }
    }

    private sealed class PositionSchema
    {
        [JsonPropertyName("asset")] public string? Asset { get; init; }
        [JsonPropertyName("allocation_pct")] public double AllocationPct { get; init; }
    }
}

/// <summary>Result of artifact validation.</summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Success() => new(true, []);
    public static ValidationResult Failure(string error) => new(false, [error]);
    public static ValidationResult Failure(IReadOnlyList<string> errors) => new(false, errors);
}
