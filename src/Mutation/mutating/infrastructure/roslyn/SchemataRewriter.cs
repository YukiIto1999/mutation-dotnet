using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>採番済み候補を schemata として元の構文木へ織り込む書換</summary>
public sealed class SchemataRewriter : CSharpSyntaxRewriter
{
    /// <summary>rollback で候補を特定するための注釈種別</summary>
    public const string AnnotationKind = "mutation.id";

    /// <summary>変異元 node から、そこへ織り込む採番済み候補への対応</summary>
    private readonly IReadOnlyDictionary<SyntaxNode, List<(int Id, MutationCandidate Candidate)>> byTarget;

    private SchemataRewriter(IReadOnlyDictionary<SyntaxNode, List<(int, MutationCandidate)>> byTarget)
    {
        this.byTarget = byTarget;
    }

    /// <summary>採番済み候補の一覧から除外分を除いて構文木全体を schemata 化</summary>
    /// <param name="root">元の構文木の根</param>
    /// <param name="numbered">連番と候補の対の列</param>
    /// <param name="disabled">rollback で無効化された連番の集合</param>
    /// <returns>schemata を織り込んだ構文木の根</returns>
    public static SyntaxNode Rewrite(
        SyntaxNode root,
        IReadOnlyList<(int Id, MutationCandidate Candidate)> numbered,
        IReadOnlySet<int> disabled
    )
    {
        var byTarget = numbered
            .Where(pair => !disabled.Contains(pair.Id))
            .GroupBy(pair => pair.Candidate.Target)
            .ToDictionary(group => group.Key, group => group.ToList());
        return new SchemataRewriter(byTarget).Visit(root)!;
    }

    /// <inheritdoc />
    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        var visited = base.Visit(node);
        if (node is null || visited is null || !byTarget.TryGetValue(node, out var pairs))
        {
            return visited;
        }

        return pairs[0].Candidate switch
        {
            MutationCandidate.ExpressionSwap => WeaveExpression((ExpressionSyntax)visited, pairs),
            MutationCandidate.StatementSwap => WeaveStatement((StatementSyntax)visited, pairs),
            MutationCandidate.BodyGuard => WeaveGuards((BlockSyntax)visited, pairs),
            MutationCandidate.VoidArrowSwap => WeaveVoidArrow(visited, pairs),
        };
    }

    /// <summary>void 式本体の block 化と if 分岐の織り込み</summary>
    private static SyntaxNode WeaveVoidArrow(SyntaxNode visited, List<(int Id, MutationCandidate Candidate)> pairs)
    {
        var arrow = ArrowOf(visited);
        if (arrow is null)
        {
            return visited;
        }

        StatementSyntax woven = SyntaxFactory.ExpressionStatement(arrow.Expression);
        foreach (var (id, candidate) in pairs)
        {
            var swap = (MutationCandidate.VoidArrowSwap)candidate;
            woven = SyntaxFactory
                .IfStatement(
                    IsActiveCall(id),
                    SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(swap.Apply(arrow.Expression))),
                    SyntaxFactory.ElseClause(AsBlock(woven))
                )
                .WithAdditionalAnnotations(Annotation(id));
        }

        return WithBlockBody(visited, SyntaxFactory.Block(woven));
    }

    /// <summary>宣言の式本体の取り出し。なければ不在</summary>
    private static ArrowExpressionClauseSyntax? ArrowOf(SyntaxNode owner) =>
        owner switch
        {
            BaseMethodDeclarationSyntax method => method.ExpressionBody,
            LocalFunctionStatementSyntax local => local.ExpressionBody,
            AccessorDeclarationSyntax accessor => accessor.ExpressionBody,
            LambdaExpressionSyntax lambda => lambda.ExpressionBody is { } body
                ? SyntaxFactory.ArrowExpressionClause(body)
                : null,
            _ => null,
        };

    /// <summary>式本体宣言の block 本体への置き換え</summary>
    private static SyntaxNode WithBlockBody(SyntaxNode owner, BlockSyntax block)
    {
        var none = SyntaxFactory.Token(SyntaxKind.None);
        return owner switch
        {
            MethodDeclarationSyntax m => m.WithExpressionBody(null).WithSemicolonToken(none).WithBody(block),
            ConstructorDeclarationSyntax c => c.WithExpressionBody(null).WithSemicolonToken(none).WithBody(block),
            DestructorDeclarationSyntax d => d.WithExpressionBody(null).WithSemicolonToken(none).WithBody(block),
            OperatorDeclarationSyntax o => o.WithExpressionBody(null).WithSemicolonToken(none).WithBody(block),
            ConversionOperatorDeclarationSyntax o => o.WithExpressionBody(null).WithSemicolonToken(none).WithBody(block),
            LocalFunctionStatementSyntax l => l.WithExpressionBody(null).WithSemicolonToken(none).WithBody(block),
            AccessorDeclarationSyntax a => a.WithExpressionBody(null).WithSemicolonToken(none).WithBody(block),
            ParenthesizedLambdaExpressionSyntax p => p.WithExpressionBody(null).WithBlock(block),
            SimpleLambdaExpressionSyntax l => l.WithExpressionBody(null).WithBlock(block),
            _ => owner,
        };
    }

    /// <summary>式候補の三項演算子の連鎖への織り込み</summary>
    private static ParenthesizedExpressionSyntax WeaveExpression(
        ExpressionSyntax visited,
        List<(int Id, MutationCandidate Candidate)> pairs
    )
    {
        var woven = Parenthesize(visited);
        foreach (var (id, candidate) in pairs)
        {
            var swap = (MutationCandidate.ExpressionSwap)candidate;
            var conditional = SyntaxFactory
                .ConditionalExpression(IsActiveCall(id), Parenthesize(swap.Apply(visited)), woven)
                .WithAdditionalAnnotations(Annotation(id));
            woven = Parenthesize(conditional);
        }

        return woven;
    }

    /// <summary>文候補の if 分岐の連鎖への織り込み</summary>
    private static BlockSyntax WeaveStatement(StatementSyntax visited, List<(int Id, MutationCandidate Candidate)> pairs)
    {
        var woven = visited;
        foreach (var (id, candidate) in pairs)
        {
            var swap = (MutationCandidate.StatementSwap)candidate;
            woven = SyntaxFactory
                .IfStatement(
                    IsActiveCall(id),
                    AsBlock(swap.Apply(visited)),
                    SyntaxFactory.ElseClause(AsBlock(woven))
                )
                .WithAdditionalAnnotations(Annotation(id));
        }

        return AsBlock(woven);
    }

    /// <summary>本体先頭への guard 分岐の織り込み</summary>
    private static BlockSyntax WeaveGuards(BlockSyntax visited, List<(int Id, MutationCandidate Candidate)> pairs)
    {
        var guards = pairs
            .Select(pair =>
                (StatementSyntax)
                    SyntaxFactory
                        .IfStatement(IsActiveCall(pair.Id), AsBlock(((MutationCandidate.BodyGuard)pair.Candidate).Guard))
                        .WithAdditionalAnnotations(Annotation(pair.Id))
            )
            .ToArray();
        return visited.WithStatements(visited.Statements.InsertRange(0, guards));
    }

    /// <summary>rollback で除去するための変異の目印の生成</summary>
    private static SyntaxAnnotation Annotation(int id) =>
        new(AnnotationKind, id.ToString(CultureInfo.InvariantCulture));

    /// <summary>MutantSwitch.IsActive 呼び出し式の生成</summary>
    private static ExpressionSyntax IsActiveCall(int id) =>
        SyntaxFactory.ParseExpression($"global::{MutantSwitchSource.IsActiveInvocation}({id})");

    /// <summary>優先順位を保つための括弧付け</summary>
    private static ParenthesizedExpressionSyntax Parenthesize(ExpressionSyntax expression) =>
        SyntaxFactory.ParenthesizedExpression(expression);

    /// <summary>単文の block への持ち上げ</summary>
    private static BlockSyntax AsBlock(StatementSyntax statement) =>
        statement as BlockSyntax ?? SyntaxFactory.Block(statement);
}
