using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

internal static class RelationalPatternSafety
{
    /// <summary>指定した関係 pattern の交換後も switch が成立するかの判定</summary>
    internal static bool IsSafeReplacement(
        SyntaxNode context,
        int patternIndex,
        SyntaxKind replacementKind,
        SemanticModel model
    )
    {
        if (context is IsPatternExpressionSyntax)
        {
            return true;
        }
        var target = context.DescendantNodesAndSelf().OfType<RelationalPatternSyntax>().ElementAt(patternIndex);
        var patterns = context switch
        {
            SwitchExpressionSyntax expression => expression.Arms
                .Select(arm => new SwitchPattern(arm.Pattern, IsCatchAll(arm.Pattern), false)),
            SwitchStatementSyntax statement => statement.Sections
                .SelectMany(section => section.Labels)
                .Select(label => label switch
                {
                    CasePatternSwitchLabelSyntax pattern => new SwitchPattern(
                        pattern.Pattern,
                        IsCatchAll(pattern.Pattern),
                        false
                    ),
                    DefaultSwitchLabelSyntax => new SwitchPattern(null, true, false),
                    _ => new SwitchPattern(null, false, true),
                }),
            _ => Enumerable.Empty<SwitchPattern>(),
        };
        var ranges = new List<NumericRange>();
        foreach (var switchPattern in patterns)
        {
            if (switchPattern.IsUnknown)
            {
                return false;
            }

            if (switchPattern.CatchesAll)
            {
                ranges.Add(NumericRange.Full);
                continue;
            }

            var operatorOverride = switchPattern.Pattern is RelationalPatternSyntax relational
                && ReferenceEquals(relational, target)
                    ? (SyntaxKind?)replacementKind
                    : null;
            if (!TryGetRange(switchPattern.Pattern, model, operatorOverride, out var range))
            {
                return false;
            }

            ranges.Add(range);
        }
        return !ContainsSubsumedPattern(ranges)
            && (ranges.Contains(NumericRange.Full) || IsCovered(ranges, NumericRange.Full));
    }
    /// <summary>pattern から整数範囲を得る処理</summary>
    private static bool TryGetRange(
        PatternSyntax? pattern,
        SemanticModel model,
        SyntaxKind? operatorOverride,
        out NumericRange range
    )
    {
        range = default;
        ExpressionSyntax expression;
        SyntaxKind? operatorKind;
        switch (pattern)
        {
            case ConstantPatternSyntax constant:
                expression = constant.Expression;
                operatorKind = null;
                break;
            case RelationalPatternSyntax relational:
                expression = relational.Expression;
                operatorKind = operatorOverride ?? relational.OperatorToken.Kind();
                break;
            default:
                return false;
        }

        var constantValue = model.GetConstantValue(expression);
        if (!constantValue.HasValue || !TryGetIntegral(constantValue.Value, out var value))
        {
            return false;
        }
        if (operatorKind is null)
        {
            range = new NumericRange(value, value);
            return true;
        }
        switch (operatorKind.Value)
        {
            case SyntaxKind.GreaterThanToken when value < long.MaxValue:
                range = new NumericRange(value + 1, long.MaxValue);
                return true;
            case SyntaxKind.GreaterThanEqualsToken:
                range = new NumericRange(value, long.MaxValue);
                return true;
            case SyntaxKind.LessThanToken when value > long.MinValue:
                range = new NumericRange(long.MinValue, value - 1);
                return true;
            case SyntaxKind.LessThanEqualsToken:
                range = new NumericRange(long.MinValue, value);
                return true;
            default:
                return false;
        }
    }
    /// <summary>定数値から整数を得る処理</summary>
    private static bool TryGetIntegral(object? value, out long integral)
    {
        var converted = value switch
        {
            sbyte number => (long)number,
            byte number => (long)number,
            short number => (long)number,
            ushort number => (long)number,
            int number => (long)number,
            uint number => (long)number,
            long number => number,
            ulong number when number <= long.MaxValue => (long)number,
            char number => number,
            _ => (long?)null,
        };
        if (converted is not { } numberValue)
        {
            integral = default;
            return false;
        }

        integral = numberValue;
        return true;
    }
    /// <summary>先行 pattern による後続 pattern の包含判定</summary>
    private static bool ContainsSubsumedPattern(IReadOnlyList<NumericRange> ranges)
    {
        var previous = new List<NumericRange>();
        foreach (var range in ranges)
        {
            if (IsCovered(previous, range))
            {
                return true;
            }

            previous.Add(range);
        }

        return false;
    }
    /// <summary>範囲列が対象範囲を覆うかの判定</summary>
    private static bool IsCovered(IEnumerable<NumericRange> ranges, NumericRange target)
    {
        var next = target.Minimum;
        foreach (var range in ranges.OrderBy(range => range.Minimum))
        {
            if (range.Maximum < next)
            {
                continue;
            }

            if (range.Minimum > next)
            {
                return false;
            }

            if (range.Maximum >= target.Maximum || range.Maximum == long.MaxValue)
            {
                return true;
            }

            next = range.Maximum + 1;
        }

        return false;
    }
    /// <summary>値をすべて受ける pattern かの判定</summary>
    private static bool IsCatchAll(PatternSyntax? pattern) =>
        pattern is null or DiscardPatternSyntax or VarPatternSyntax;

    private readonly record struct SwitchPattern(PatternSyntax? Pattern, bool CatchesAll, bool IsUnknown);

    private readonly record struct NumericRange(long Minimum, long Maximum)
    {
        public static NumericRange Full { get; } = new(long.MinValue, long.MaxValue);
    }

}
