using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>static 初期化の実行区間を切替クラスへ通知する計装の織り込み</summary>
public sealed class StaticContextRewriter : CSharpSyntaxRewriter
{
    /// <summary>計装箇所を読込時から書換後まで辿るための注釈種別</summary>
    public const string AnnotationKind = "mutation.static";

    /// <summary>目印の鍵から、その static 初期化を持つ型の完全修飾名への対応</summary>
    private readonly IReadOnlyDictionary<string, string> initializerTypes;

    /// <summary>rollback で計装を外した目印の鍵の集合</summary>
    private readonly IReadOnlySet<string> disabled;

    private StaticContextRewriter(IReadOnlyDictionary<string, string> initializerTypes, IReadOnlySet<string> disabled)
    {
        this.initializerTypes = initializerTypes;
        this.disabled = disabled;
    }

    /// <summary>static 初期化に当たる node への読込時の目印付け</summary>
    /// <param name="root">解析直後の構文木の根</param>
    /// <returns>目印付きの根。対象がなければ元のまま</returns>
    public static SyntaxNode Annotate(SyntaxNode root)
    {
        var targets = root.DescendantNodes().Where(IsStaticInitialization).ToList();
        if (targets.Count == 0)
        {
            return root;
        }

        var next = 0;
        return root.ReplaceNodes(
            targets,
            (_, rewritten) =>
                rewritten.WithAdditionalAnnotations(
                    new SyntaxAnnotation(AnnotationKind, (next++).ToString(CultureInfo.InvariantCulture))
                )
        );
    }

    /// <summary>目印付き node の鍵</summary>
    /// <param name="node">読込時に目印を付けた node</param>
    /// <returns>鍵。目印がなければ不在</returns>
    public static string? KeyOf(SyntaxNode node) => node.GetAnnotations(AnnotationKind).FirstOrDefault()?.Data;

    /// <summary>schemata 済みの構文木への static 追跡の計装の追加</summary>
    /// <param name="root">schemata を織り込んだ構文木の根</param>
    /// <param name="initializerTypes">初期化子の鍵から包むときの型名への対応</param>
    /// <param name="disabled">rollback で無効化された計装の鍵の集合</param>
    /// <returns>計装を足した構文木の根</returns>
    public static SyntaxNode Rewrite(
        SyntaxNode root,
        IReadOnlyDictionary<string, string> initializerTypes,
        IReadOnlySet<string> disabled
    ) => new StaticContextRewriter(initializerTypes, disabled).Visit(root) ?? root;

    /// <inheritdoc />
    public override SyntaxNode? VisitEqualsValueClause(EqualsValueClauseSyntax node)
    {
        var visited = (EqualsValueClauseSyntax)base.VisitEqualsValueClause(node)!;
        var key = KeyOf(node);
        if (key is null || disabled.Contains(key) || !initializerTypes.TryGetValue(key, out var typeName))
        {
            return visited;
        }

        var lambda = SyntaxFactory.ParenthesizedLambdaExpression(visited.Value);
        var wrapped = SyntaxFactory.ParseExpression(
            $"global::{MutantSwitchSource.TrackStaticInvocation}<{typeName}>({lambda.NormalizeWhitespace()})"
        );
        return visited.WithValue(wrapped);
    }

    /// <inheritdoc />
    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var visited = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
        var key = KeyOf(node);
        return key is null || disabled.Contains(key) || visited.Body is null
            ? visited
            : visited.WithBody(WrapBody(visited.Body));
    }

    /// <inheritdoc />
    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
        var key = KeyOf(node);
        return key is null || disabled.Contains(key) || visited.Body is null
            ? visited
            : visited.WithBody(WrapBody(visited.Body));
    }

    /// <summary>static 初期化の構文かの判定</summary>
    private static bool IsStaticInitialization(SyntaxNode node) =>
        node switch
        {
            ConstructorDeclarationSyntax ctor => ctor.Modifiers.Any(SyntaxKind.StaticKeyword),
            MethodDeclarationSyntax method => method
                .AttributeLists.SelectMany(l => l.Attributes)
                .Any(a => a.Name.ToString().Contains("ModuleInitializer", StringComparison.Ordinal)),
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field } } =>
                field.Modifiers.Any(SyntaxKind.StaticKeyword) && !field.Modifiers.Any(SyntaxKind.ConstKeyword),
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => property.Modifiers.Any(
                SyntaxKind.StaticKeyword
            ),
            _ => false,
        };

    /// <summary>本体の EnterStatic / ExitStatic での包み込み</summary>
    private static BlockSyntax WrapBody(BlockSyntax body)
    {
        var enter = SyntaxFactory.ParseStatement($"global::{MutantSwitchSource.EnterStaticInvocation}();");
        var exit = SyntaxFactory.ParseStatement($"global::{MutantSwitchSource.ExitStaticInvocation}();");
        var guarded = SyntaxFactory.TryStatement(
            SyntaxFactory.Block(body.Statements),
            default,
            SyntaxFactory.FinallyClause(SyntaxFactory.Block(exit))
        );
        return SyntaxFactory.Block(enter, guarded);
    }
}
