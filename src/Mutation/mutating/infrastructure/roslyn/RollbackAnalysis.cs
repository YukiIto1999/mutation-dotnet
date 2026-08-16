using Mutation.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>compile error の位置から無効化すべき注入箇所を逆引きする解析</summary>
public static class RollbackAnalysis
{
    /// <summary>error 位置を最内側から遡って最初に見つかる注入の目印の収集</summary>
    /// <param name="errors">emit が返した error 診断の列</param>
    /// <returns>無効化する変異の連番と static 計装の鍵</returns>
    public static RollbackCulprits FindCulprits(IReadOnlyList<Diagnostic> errors)
    {
        var mutants = new HashSet<int>();
        var statics = new HashSet<string>();
        foreach (var location in errors.Select(error => error.Location))
        {
            if (location.SourceTree is not { } tree)
            {
                continue;
            }

            var node = tree.GetRoot().FindNode(location.SourceSpan, getInnermostNodeForTie: true);
            Attribute(node, mutants, statics);
        }

        return new RollbackCulprits(mutants, statics);
    }

    /// <summary>一つの error 位置の、最寄りの変異か static 計装への帰属</summary>
    private static void Attribute(SyntaxNode node, HashSet<int> mutants, HashSet<string> statics)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var mutant = ancestor.GetAnnotations(SchemataRewriter.AnnotationKind).FirstOrDefault();
            if (mutant?.Data is { } data && int.TryParse(data, out var id))
            {
                mutants.Add(id);
                return;
            }

            if (StaticContextRewriter.KeyOf(ancestor) is { } key)
            {
                statics.Add(key);
                return;
            }
        }

        AttributeToEnclosingMember(node, mutants, statics);
    }

    /// <summary>目印が見つからないときの、包含 member 内の全注入への帰属</summary>
    private static void AttributeToEnclosingMember(SyntaxNode node, HashSet<int> mutants, HashSet<string> statics)
    {
        var member = node.AncestorsAndSelf().FirstOrDefault(n => n is MemberDeclarationSyntax);
        if (member is null)
        {
            return;
        }

        foreach (var descendant in member.DescendantNodesAndSelf())
        {
            var mutant = descendant.GetAnnotations(SchemataRewriter.AnnotationKind).FirstOrDefault();
            if (mutant?.Data is { } data && int.TryParse(data, out var id))
            {
                mutants.Add(id);
            }

            if (StaticContextRewriter.KeyOf(descendant) is { } key)
            {
                statics.Add(key);
            }
        }
    }
}

/// <summary>rollback で無効化する注入箇所の一覧</summary>
/// <param name="MutantIds">無効化する変異の連番</param>
/// <param name="StaticKeys">無効化する static 計装の鍵</param>
public sealed record RollbackCulprits(IReadOnlySet<int> MutantIds, IReadOnlySet<string> StaticKeys)
{
    /// <summary>無効化できる箇所が一つもないか</summary>
    public bool IsEmpty => MutantIds.Count == 0 && StaticKeys.Count == 0;
}
