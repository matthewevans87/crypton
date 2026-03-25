using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgentRunner.StateMachine;

namespace AgentRunner.Artifacts;

public interface IArtifactValidator
{
    ArtifactValidationResult Validate(string content);
    string ArtifactType { get; }
}

public class ArtifactValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static ArtifactValidationResult Success() => new() { IsValid = true };
    public static ArtifactValidationResult Failure(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors.ToList()
    };
}

public class ArtifactValidators
{
    public static IArtifactValidator ForState(LoopState state) => state switch
    {
        LoopState.Plan => new PlanArtifactValidator(),
        LoopState.Research => new ResearchArtifactValidator(),
        LoopState.Analyze => new AnalysisArtifactValidator(),
        LoopState.Synthesize => new StrategyArtifactValidator(),
        LoopState.Evaluate => new EvaluationArtifactValidator(),
        _ => throw new ArgumentException($"No validator for state: {state}")
    };
}

public class PlanArtifactValidator : IArtifactValidator
{
    public string ArtifactType => "plan.md";

    private static readonly string[] RequiredSections =
    {
        "## 1. Meta-Signals",
        "## 2. Macro Market Conditions",
        "## 3. Technical Signals",
        "## 4. On-Chain Signals",
        "## 5. News & Social Signals",
        "## 6. Research Agenda",
        "## 7. Signals Deprioritized"
    };

    public ArtifactValidationResult Validate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ArtifactValidationResult.Failure("Plan content is empty");

        var result = new ArtifactValidationResult { IsValid = true };

        foreach (var section in RequiredSections)
        {
            if (!content.Contains(section))
            {
                result.Errors.Add($"Missing required section: {section}");
                result.IsValid = false;
            }
        }

        if (content.Length < 500)
            result.Warnings.Add("Plan content seems unusually short");

        return result;
    }
}

public class ResearchArtifactValidator : IArtifactValidator
{
    public string ArtifactType => "research.md";

    public ArtifactValidationResult Validate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ArtifactValidationResult.Failure("Research content is empty");

        var result = new ArtifactValidationResult { IsValid = true };

        if (!content.Contains("## Executive Summary", StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add("Missing Executive Summary section");

        if (!content.Contains("## Investigation Findings", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("Missing Investigation Findings section");
            result.IsValid = false;
        }

        if (content.Length < 500)
            result.Warnings.Add("Research content seems unusually short");

        var hasVerdicts = content.Contains("Confirmed") ||
                          content.Contains("Contradicted") ||
                          content.Contains("Inconclusive");
        if (!hasVerdicts)
            result.Warnings.Add("No verdicts (Confirmed/Contradicted/Inconclusive) found");

        return result;
    }
}

public class AnalysisArtifactValidator : IArtifactValidator
{
    public string ArtifactType => "analysis.md";

    public ArtifactValidationResult Validate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ArtifactValidationResult.Failure("Analysis content is empty");

        var result = new ArtifactValidationResult { IsValid = true };

        if (!content.Contains("## Market Overview", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("Missing Market Overview section");
            result.IsValid = false;
        }

        if (!content.Contains("## Per-Asset Analysis", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("Missing Per-Asset Analysis section");
            result.IsValid = false;
        }

        if (!content.Contains("## Current Position Assessment", StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add("Missing Current Position Assessment section");

        if (!content.Contains("## Risk Matrix", StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add("Missing Risk Matrix section");

        if (content.Length < 500)
            result.Warnings.Add("Analysis content seems unusually short");

        return result;
    }
}

public class StrategyArtifactValidator : IArtifactValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ArtifactType => "strategy.json";

    public ArtifactValidationResult Validate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ArtifactValidationResult.Failure("Strategy content is empty");

        var result = new ArtifactValidationResult { IsValid = true };

        JsonDocument? jsonDoc = null;
        try
        {
            jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"Invalid JSON: {ex.Message}");
            result.IsValid = false;
            return result;
        }
        finally
        {
            jsonDoc?.Dispose();
        }

        try
        {
            var strategy = JsonSerializer.Deserialize<StrategySchema>(content, JsonOptions);

            if (string.IsNullOrEmpty(strategy?.Mode))
            {
                result.Errors.Add("Missing required field: mode");
                result.IsValid = false;
            }
            else if (strategy.Mode != "paper" && strategy.Mode != "live")
            {
                result.Errors.Add($"Invalid mode: {strategy.Mode}. Must be 'paper' or 'live'");
                result.IsValid = false;
            }

            if (string.IsNullOrEmpty(strategy?.ValidityWindow))
            {
                result.Errors.Add("Missing required field: validityWindow");
                result.IsValid = false;
            }
            else if (!DateTime.TryParse(strategy.ValidityWindow, out _))
            {
                result.Errors.Add($"Invalid validityWindow format: {strategy.ValidityWindow}");
                result.IsValid = false;
            }

            if (string.IsNullOrEmpty(strategy?.Posture))
            {
                result.Errors.Add("Missing required field: posture");
                result.IsValid = false;
            }

            if (strategy?.PortfolioRisk == null)
            {
                result.Errors.Add("Missing required field: portfolioRisk");
                result.IsValid = false;
            }
            else
            {
                if (strategy.PortfolioRisk.MaxDrawdownPct < 0 || strategy.PortfolioRisk.MaxDrawdownPct > 1)
                    result.Errors.Add("portfolioRisk.maxDrawdownPct must be between 0 and 1");
                if (strategy.PortfolioRisk.DailyLossLimitUsd < 0)
                    result.Errors.Add("portfolioRisk.dailyLossLimitUsd must be non-negative");
                if (strategy.PortfolioRisk.MaxTotalExposurePct < 0 || strategy.PortfolioRisk.MaxTotalExposurePct > 1)
                    result.Errors.Add("portfolioRisk.maxTotalExposurePct must be between 0 and 1");
                if (strategy.PortfolioRisk.MaxPerPositionPct < 0 || strategy.PortfolioRisk.MaxPerPositionPct > 1)
                    result.Errors.Add("portfolioRisk.maxPerPositionPct must be between 0 and 1");

                if (result.Errors.Any(e => e.StartsWith("portfolioRisk.")))
                    result.IsValid = false;
            }

            if (strategy?.Positions == null)
            {
                result.Errors.Add("Missing required field: positions");
                result.IsValid = false;
            }
            else
            {
                foreach (var pos in strategy.Positions)
                {
                    if (string.IsNullOrEmpty(pos.Asset))
                        result.Errors.Add("position.asset is required");
                    if (pos.AllocationPct < 0 || pos.AllocationPct > 1)
                        result.Errors.Add($"position {pos.Asset}: allocationPct must be between 0 and 1");
                }
                if (result.Errors.Any(e => e.StartsWith("position")))
                    result.IsValid = false;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Schema validation error: {ex.Message}");
            result.IsValid = false;
        }

        return result;
    }
}

public class EvaluationArtifactValidator : IArtifactValidator
{
    public string ArtifactType => "evaluation.md";

    public ArtifactValidationResult Validate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ArtifactValidationResult.Failure("Evaluation content is empty");

        var result = new ArtifactValidationResult { IsValid = true };

        if (!content.Contains("## Performance Metrics", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("Missing Performance Metrics section");
            result.IsValid = false;
        }

        if (!content.Contains("## Trade Review", StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add("Missing Trade Review section");

        if (!content.Contains("## Overall Assessment", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("Missing Overall Assessment section");
            result.IsValid = false;
        }

        if (!content.Contains("## Recommendations for Next Cycle", StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add("Missing Recommendations for Next Cycle section");

        if (content.Length < 300)
            result.Warnings.Add("Evaluation content seems unusually short");

        return result;
    }
}

public class StrategySchema
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("validityWindow")]
    public string? ValidityWindow { get; set; }

    [JsonPropertyName("posture")]
    public string? Posture { get; set; }

    [JsonPropertyName("postureRationale")]
    public string? PostureRationale { get; set; }

    [JsonPropertyName("portfolioRisk")]
    public RiskSchema? PortfolioRisk { get; set; }

    [JsonPropertyName("positions")]
    public List<PositionSchema>? Positions { get; set; }
}

public class RiskSchema
{
    [JsonPropertyName("maxDrawdownPct")]
    public double MaxDrawdownPct { get; set; }

    [JsonPropertyName("dailyLossLimitUsd")]
    public double DailyLossLimitUsd { get; set; }

    [JsonPropertyName("maxTotalExposurePct")]
    public double MaxTotalExposurePct { get; set; }

    [JsonPropertyName("maxPerPositionPct")]
    public double MaxPerPositionPct { get; set; }
}

public class PositionSchema
{
    [JsonPropertyName("asset")]
    public string? Asset { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("allocationPct")]
    public double AllocationPct { get; set; }

    [JsonPropertyName("entryType")]
    public string? EntryType { get; set; }
}
