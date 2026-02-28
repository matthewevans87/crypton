namespace Crypton.Api.ExecutionService.Strategy.Conditions;

/// <summary>
/// Parses DSL condition strings into compiled ICondition trees.
/// All parsing happens at strategy load time; any parse error is a
/// ConditionParseException, not a runtime error.
/// </summary>
public sealed class ConditionParser
{
    public ICondition Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ConditionParseException("Expression is empty.");

        var input = expression.Trim();
        var (condition, remainder) = ParseExpression(input);

        if (!string.IsNullOrWhiteSpace(remainder))
            throw new ConditionParseException($"Unexpected trailing content: '{remainder}'");

        return condition;
    }

    private static (ICondition condition, string remainder) ParseExpression(string input)
    {
        input = input.Trim();

        // Composite: AND(...), OR(...), NOT(...)
        if (input.StartsWith("AND(", StringComparison.OrdinalIgnoreCase))
            return ParseComposite(input, "AND");
        if (input.StartsWith("OR(", StringComparison.OrdinalIgnoreCase))
            return ParseComposite(input, "OR");
        if (input.StartsWith("NOT(", StringComparison.OrdinalIgnoreCase))
            return ParseComposite(input, "NOT");

        // Leaf: operand operator value, or operand crosses_above/below value
        return ParseLeaf(input);
    }

    private static (ICondition, string) ParseComposite(string input, string keyword)
    {
        var prefix = keyword + "(";
        var content = ExtractParenthesisContent(input[prefix.Length..], out var afterClose);
        var children = SplitTopLevel(content);

        if (keyword == "NOT")
        {
            if (children.Count != 1)
                throw new ConditionParseException("NOT() requires exactly one argument.");
            var (inner, _) = ParseExpression(children[0]);
            return (new NotCondition(inner), afterClose);
        }

        if (children.Count < 2)
            throw new ConditionParseException($"{keyword}() requires at least two arguments.");

        var conditions = children.Select(c => ParseExpression(c).condition).ToList();
        ICondition result = keyword == "AND"
            ? new AndCondition(conditions)
            : new OrCondition(conditions);

        return (result, afterClose);
    }

    private static (ICondition, string) ParseLeaf(string input)
    {
        // Possible formats:
        // price(ASSET) OP VALUE
        // INDICATOR(PERIOD, ASSET) OP VALUE
        // price(ASSET) crosses_above VALUE
        // INDICATOR(PERIOD, ASSET) crosses_below VALUE

        // Extract operand (everything up to the first space after any closing paren)
        var parenOpen = input.IndexOf('(');
        if (parenOpen < 0)
            throw new ConditionParseException($"Cannot parse leaf expression: '{input}'");

        var funcName = input[..parenOpen].Trim();
        var argsContent = ExtractParenthesisContent(input[(parenOpen + 1)..], out var afterParen);
        var args = SplitTopLevel(argsContent);

        afterParen = afterParen.Trim();

        // Determine operator and value
        string op;
        decimal value;
        bool isCrossingAbove = false, isCrossing = false;

        if (afterParen.StartsWith("crosses_above ", StringComparison.OrdinalIgnoreCase))
        {
            isCrossing = true; isCrossingAbove = true;
            value = ParseDecimal(afterParen["crosses_above ".Length..].Trim());
            op = ">";
        }
        else if (afterParen.StartsWith("crosses_below ", StringComparison.OrdinalIgnoreCase))
        {
            isCrossing = true;
            value = ParseDecimal(afterParen["crosses_below ".Length..].Trim());
            op = "<";
        }
        else
        {
            // Standard comparison
            int spaceIdx = afterParen.IndexOf(' ');
            if (spaceIdx < 0) throw new ConditionParseException($"Cannot find operator in: '{afterParen}'");
            op = afterParen[..spaceIdx].Trim();
            var valueStr = afterParen[(spaceIdx + 1)..].Trim();
            // Strip remainder after the value token
            var nextSpace = valueStr.IndexOf(' ');
            var remainder2 = nextSpace >= 0 ? valueStr[nextSpace..] : string.Empty;
            valueStr = nextSpace >= 0 ? valueStr[..nextSpace] : valueStr;
            value = ParseDecimal(valueStr);

            ICondition leaf2 = BuildLeafCondition(funcName, args, op, value);
            return (leaf2, remainder2);
        }

        ICondition leaf = BuildLeafCondition(funcName, args, op, value);

        if (isCrossing)
            leaf = new CrossingCondition(leaf, isCrossingAbove);

        return (leaf, string.Empty);
    }

    private static ICondition BuildLeafCondition(string funcName, List<string> args, string op, decimal value)
    {
        if (funcName.Equals("price", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count != 1) throw new ConditionParseException("price() requires exactly one argument (asset).");
            return new PriceComparisonCondition(args[0].Trim(), op, value);
        }

        // Must be an indicator
        if (args.Count < 1) throw new ConditionParseException($"Indicator {funcName}() requires at least one argument.");

        // Last arg is asset; prior args are indicator parameters
        var asset = args[^1].Trim();
        var indicatorParams = args.Take(args.Count - 1).Select(a => a.Trim()).ToArray();
        var key = BuildIndicatorKey(funcName, indicatorParams);

        return new IndicatorComparisonCondition(key, asset, op, value);
    }

    /// <summary>Build a canonical indicator key used in MarketSnapshot.Indicators dictionary.</summary>
    public static string BuildIndicatorKey(string indicatorName, string[] periods)
    {
        if (periods.Length == 0) return indicatorName.ToUpperInvariant();
        return $"{indicatorName.ToUpperInvariant()}_{string.Join("_", periods)}";
    }

    private static string ExtractParenthesisContent(string input, out string afterClose)
    {
        var depth = 0;
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '(') depth++;
            else if (input[i] == ')')
            {
                if (depth == 0) { afterClose = input[(i + 1)..]; return input[..i]; }
                depth--;
            }
        }
        throw new ConditionParseException("Unmatched parenthesis.");
    }

    private static List<string> SplitTopLevel(string input)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '(') depth++;
            else if (input[i] == ')') depth--;
            else if (input[i] == ',' && depth == 0)
            {
                result.Add(input[start..i].Trim());
                start = i + 1;
            }
        }
        result.Add(input[start..].Trim());
        return result;
    }

    private static decimal ParseDecimal(string s)
    {
        s = s.Trim();
        if (!decimal.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d))
            throw new ConditionParseException($"Cannot parse decimal value: '{s}'");
        return d;
    }
}

public sealed class ConditionParseException : Exception
{
    public ConditionParseException(string message) : base(message) { }
}
