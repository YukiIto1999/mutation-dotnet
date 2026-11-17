using TypeModeling.Domain;

namespace Mutation.Shared;

/// <summary>`--since` の実行で、対象の変異候補をどの範囲に選んだかの記録</summary>
/// <param name="Scope">選んだ範囲</param>
/// <param name="Reasons">範囲を決めた、対象へ届く変更の関係とその file</param>
/// <param name="Unanalysed">選んだ範囲の外にあり、分析していない影響の関係とその file。明示した部分集合のときだけ現れる</param>
/// <param name="NotInputs">対象の入力ではないと判断し、影響なしとした変更 file</param>
public sealed record CandidateSelection(
    SelectionScope Scope,
    IReadOnlyList<ImpactReason> Reasons,
    IReadOnlyList<ImpactReason> Unanalysed,
    IReadOnlyList<string> NotInputs
);

/// <summary>変異候補を選んだ範囲</summary>
[Closed]
public abstract record SelectionScope
{
    /// <summary>列挙した派生以外の範囲を塞ぐ基底の構築</summary>
    private SelectionScope()
    {
    }

    /// <summary>報告に書く範囲の名前</summary>
    public abstract string Name { get; }

    /// <summary>対象の production の全範囲。変更が対象へ届き、届く先を file より細かく選べないため</summary>
    public sealed record FullProduction : SelectionScope
    {
        /// <inheritdoc />
        public override string Name => "full-production";
    }

    /// <summary>変更した production の変更行に重なる変異だけの、明示した部分集合。影響する範囲の分析ではない</summary>
    public sealed record ExplicitSubset : SelectionScope
    {
        /// <inheritdoc />
        public override string Name => "changed-lines";
    }

    /// <summary>対象の入力に届く変更がなく、候補が空の範囲</summary>
    public sealed record NoImpact : SelectionScope
    {
        /// <inheritdoc />
        public override string Name => "no-impact";
    }
}

/// <summary>変更した file と対象の関係</summary>
/// <param name="Relation">関係の種類</param>
/// <param name="Paths">その関係にある変更 file の絶対 path</param>
public sealed record ImpactReason(ImpactRelation Relation, IReadOnlyList<string> Paths);

/// <summary>変更した file が対象へ届く関係の種類</summary>
[Closed]
public abstract record ImpactRelation
{
    /// <summary>列挙した派生以外の関係を塞ぐ基底の構築</summary>
    private ImpactRelation()
    {
    }

    /// <summary>報告に書く関係の名前</summary>
    public abstract string Name { get; }

    /// <summary>対象 project のソースの変更</summary>
    public sealed record ProductionChanged : ImpactRelation
    {
        /// <inheritdoc />
        public override string Name => "production-changed";
    }

    /// <summary>変更したソースの未変更の呼び出し元。file より細かい関係を選べないので、対象の全範囲か未分析になる</summary>
    public sealed record CallersOfChangedProduction : ImpactRelation
    {
        /// <inheritdoc />
        public override string Name => "callers-of-changed-production";
    }

    /// <summary>対にしたテスト project のソースの変更。変えたテストが検出する production は選べない</summary>
    public sealed record TestChanged : ImpactRelation
    {
        /// <inheritdoc />
        public override string Name => "test-changed";
    }

    /// <summary>対象かテストが参照する別の project のソースの変更</summary>
    public sealed record DependencyChanged : ImpactRelation
    {
        /// <inheritdoc />
        public override string Name => "dependency-changed";
    }

    /// <summary>build の設定、依存の版、analyzer、additional file、resource、project file など、対象かテストの build へ届く入力の変更</summary>
    public sealed record ConfigChanged : ImpactRelation
    {
        /// <inheritdoc />
        public override string Name => "config-changed";
    }

    /// <summary>基点にあって消えた file。変更前の参照を追えないので、届く先が確定しない</summary>
    public sealed record FileRemoved : ImpactRelation
    {
        /// <inheritdoc />
        public override string Name => "file-removed";
    }

    /// <summary>対象かテストの project の中にあるが、ソースでも build の入力でもない file の変更。実行時に読まれるかを確定できない</summary>
    public sealed record UnknownInput : ImpactRelation
    {
        /// <inheritdoc />
        public override string Name => "unknown-input";
    }
}
