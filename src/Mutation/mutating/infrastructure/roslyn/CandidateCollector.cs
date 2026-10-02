using Mutation.Mutating.Domain;
using Mutation.Mutating.Infrastructure.Roslyn.Operators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;


namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>一ファイルの構文木からの変異候補の収集</summary>
public static class CandidateCollector
{
    /// <summary>既定の演算子一式</summary>
    public static IReadOnlyList<IMutationOperator> DefaultOperators { get; } =
        [
            new BinaryOperatorMutator(),
            new RelationalPatternMutator(),
            new AssignmentMutator(),
            new UnaryMutator(),
            new LiteralMutator(),
            new ConditionMutator(),
            new LinqMethodMutator(),
            new StringMethodMutator(),
            new MathMethodMutator(),
            new InitializerMutator(),
            new NullCoalesceMutator(),
            new StatementRemover(),
            new MethodBodyNullifier(),
        ];

    /// <summary>構文木を一度走査して、選別方針が許す候補を文書順で列挙</summary>
    /// <param name="root">対象ファイルの構文木の根</param>
    /// <param name="model">対象ファイルの意味解析</param>
    /// <param name="operators">適用する演算子の列</param>
    /// <param name="policy">演算子と呼出名の選別方針</param>
    /// <returns>注入可能と判定された候補の列</returns>
    public static IReadOnlyList<MutationCandidate> Collect(
        SyntaxNode root,
        SemanticModel model,
        IReadOnlyList<IMutationOperator> operators,
        MutationPolicy policy
    )
    {
        var allowed = operators.Where(o => policy.AllowsOperator(o.Name)).ToArray();
        var candidates = new List<MutationCandidate>();
        var filterLines = policy.ChangedLinesOnly;
        var filePath = root.SyntaxTree.FilePath;
        foreach (var node in root.DescendantNodesAndSelf())
        {
            if (filterLines)
            {
                var nodeSpan = node.GetLocation().GetLineSpan();
                if (!policy.IncludesSpan(
                    filePath, nodeSpan.StartLinePosition.Line + 1, nodeSpan.EndLinePosition.Line + 1))
                {
                    continue;
                }
            }

            foreach (var mutationOperator in allowed)
            {
                candidates.AddRange(
                    mutationOperator
                        .Candidates(node, model)
                        .Where(candidate => !filterLines || IncludesChangedLine(candidate, policy, filePath))
                        .Where(candidate => IsAdmissible(candidate, model) && !InsideIgnoredCall(candidate, policy))
                        .Select(candidate => StatementContexts.Normalize(candidate, model))
                        .OfType<MutationCandidate>()
                );
            }
        }

        return candidates;
    }

    /// <summary>報告範囲と変わった行の重なり</summary>
    /// <param name="candidate">選別対象の変異</param>
    /// <param name="policy">変更行の選別方針</param>
    /// <param name="filePath">対象の構文木の絶対 path</param>
    /// <returns>報告範囲が変更行と重なるなら真</returns>
    private static bool IncludesChangedLine(MutationCandidate candidate, MutationPolicy policy, string filePath)
    {
        var span = candidate.ReportTarget.GetLocation().GetLineSpan();
        return policy.IncludesSpan(filePath, span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
    }

    /// <summary>無視指定の呼び出しの引数の中かの判定</summary>
    private static bool InsideIgnoredCall(MutationCandidate candidate, MutationPolicy policy)
    {
        if (!policy.HasIgnoredMethods)
        {
            return false;
        }

        var start = candidate.Target is ExpressionStatementSyntax statement ? statement.Expression : candidate.Target;
        return start
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => policy.IgnoresMethod(CalleeName(invocation)))
            || (start is AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaited }
                && policy.IgnoresMethod(CalleeName(awaited)));
    }

    /// <summary>呼び出し先の単純名の取り出し</summary>
    private static string CalleeName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => "",
        };

    /// <summary>文脈の禁則と意味検査を通る候補かの判定</summary>
    private static bool IsAdmissible(MutationCandidate candidate, SemanticModel model) =>
        candidate switch
        {
            MutationCandidate.ExpressionSwap swap => !MutationContexts.IsForbidden(swap.Expression, model)
                && (swap.AllowsExpressionHazard || !MutationContexts.HasExpressionHazard(swap.Expression)),
            MutationCandidate.StatementSwap swap => !MutationContexts.IsForbidden(swap.Statement, model),
            MutationCandidate.BodyGuard => true,
            MutationCandidate.VoidArrowSwap => true,
        };
}
