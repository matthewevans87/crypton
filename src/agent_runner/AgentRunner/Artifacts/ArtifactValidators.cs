using System.Text.Json;
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

            if (string.IsNullOrEmpty(strategy?.ValidUntil))
            {
                result.Errors.Add("Missing required field: validUntil");
                result.IsValid = false;
            }
            else if (!DateTime.TryParse(strategy.ValidUntil, out _))
            {
                result.Errors.Add($"Invalid validUntil format: {strategy.ValidUntil}");
                result.IsValid = false;
            }

            if (string.IsNullOrEmpty(strategy?.Posture))
            {
                result.Errors.Add("Missing required field: posture");
                result.IsValid = false;
            }

            if (strategy?.Risk == null)
            {
                result.Errors.Add("Missing required field: risk");
                result.IsValid = false;
            }
            else
            {
                if (strategy.Risk.MaxDrawdown < 0 || strategy.Risk.MaxDrawdown > 1)
                    result.Errors.Add("risk.maxDrawdown must be between 0 and 1");
                if (strategy.Risk.DailyLossLimit < 0 || strategy.Risk.DailyLossLimit > 1)
                    result.Errors.Add("risk.dailyLossLimit must be between 0 and 1");
                if (strategy.Risk.MaxExposure < 0 || strategy.Risk.MaxExposure > 1)
                    result.Errors.Add("risk.maxExposure must be between 0 and 1");
                if (strategy.Risk.MaxPositionSize < 0 || strategy.Risk.MaxPositionSize > 1)
                    result.Errors.Add("risk.maxPositionSize must be between 0 and 1");

                if (result.Errors.Any(e => e.StartsWith("risk.")))
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
                    if (pos.Allocation < 0 || pos.Allocation > 1)
                        result.Errors.Add($"position {pos.Asset}: allocation must be between 0 and 1");
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
    public string? Mode { get; set; }
    public string? ValidUntil { get; set; }
    public string? Posture { get; set; }
    public string? Rationale { get; set; }
    public RiskSchema? Risk { get; set; }
    public List<PositionSchema>? Positions { get; set; }
}

public class RiskSchema
{
    public double MaxDrawdown { get; set; }
    public double DailyLossLimit { get; set; }
    public double MaxExposure { get; set; }
    public double MaxPositionSize { get; set; }
}

public class PositionSchema
{
    public string? Asset { get; set; }
    public string? Direction { get; set; }
    public double Allocation { get; set; }
    public string? EntryType { get; set; }
}
