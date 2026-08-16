

namespace Mutation.Mutating.Domain;

/// <summary>どこを変異させるかを一手に決める選別方針</summary>
public sealed class MutationPolicy
{
    /// <summary>含めるファイルの glob 範囲</summary>
    private readonly MutationScope scope;

    /// <summary>差分運用の対象ファイル集合。絞らないなら不在</summary>
    private readonly IReadOnlySet<string>? changedFiles;

    /// <summary>除外する変異演算子の名前集合</summary>
    private readonly IReadOnlySet<string> ignoredOperators;

    /// <summary>その呼び出しの中を変異させない method 名の集合</summary>
    private readonly IReadOnlySet<string> ignoredMethods;

    /// <summary>選別方針の組み立て</summary>
    /// <param name="scope">含めるファイルの glob 範囲</param>
    /// <param name="changedFiles">差分運用で対象を絞るときの、変更ファイルの絶対 path 集合。絞らないなら不在</param>
    /// <param name="ignoredOperators">除外する変異演算子の名前集合</param>
    /// <param name="ignoredMethods">その呼び出しの中を変異させない method 名の集合</param>
    public MutationPolicy(
        MutationScope scope,
        IReadOnlySet<string>? changedFiles,
        IReadOnlySet<string> ignoredOperators,
        IReadOnlySet<string> ignoredMethods
    )
    {
        this.scope = scope;
        this.changedFiles = changedFiles;
        this.ignoredOperators = ignoredOperators;
        this.ignoredMethods = ignoredMethods;
    }

    /// <summary>全ファイルを全演算子で対象にする方針</summary>
    public static MutationPolicy Everything { get; } =
        new(
            MutationScope.FromPatterns([]),
            changedFiles: null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal)
        );

    /// <summary>呼出名の選別を使うかの判定。偽なら構文走査の省略が許される</summary>
    public bool HasIgnoredMethods => ignoredMethods.Count > 0;

    /// <summary>ファイルが変異対象に含まれるか。glob 範囲と差分絞りの両方を満たすこと</summary>
    /// <param name="absolutePath">ファイルの絶対 path</param>
    /// <param name="relativePath">project directory からの相対 path</param>
    /// <returns>含めるなら真</returns>
    public bool IncludesFile(string absolutePath, string relativePath) =>
        scope.Includes(relativePath) && (changedFiles is null || changedFiles.Contains(Path.GetFullPath(absolutePath)));

    /// <summary>演算子が変異生成に使えるか</summary>
    /// <param name="operatorName">変異演算子の名前。大文字小文字は区別しない</param>
    /// <returns>使えるなら真</returns>
    public bool AllowsOperator(string operatorName) => !ignoredOperators.Contains(operatorName);

    /// <summary>呼出名が「中を変異させない」対象か</summary>
    /// <param name="methodName">呼び出しの単純名</param>
    /// <returns>除外対象なら真</returns>
    public bool IgnoresMethod(string methodName) => ignoredMethods.Contains(methodName);
}
