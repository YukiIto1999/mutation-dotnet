using TypeModeling.Domain;

namespace Mutation.Verifying.Domain;

/// <summary>実行中の進行を伝える出来事</summary>
[ClosedUnion]
public abstract record RunProgress
{
    /// <summary>列挙した派生以外の出来事を塞ぐ基底の構築</summary>
    private RunProgress()
    {
    }

    /// <summary>対象の build が終わった</summary>
    /// <param name="Ms">build の所要時間</param>
    public sealed record BuildCompleted(double Ms) : RunProgress;

    /// <summary>保存と生成入力の完全一致による、前回の変異と判定の再利用</summary>
    public sealed record SnapshotMatched : RunProgress;

    /// <summary>変異の生成とコンパイルが終わった</summary>
    /// <param name="Mutants">生成した変異の件数</param>
    /// <param name="CompileErrors">rollback で無効化された変異の件数</param>
    /// <param name="MutateMs">候補収集の所要時間</param>
    /// <param name="CompileMs">コンパイルの所要時間</param>
    public sealed record MutantsGenerated(int Mutants, int CompileErrors, double MutateMs, double CompileMs) : RunProgress;

    /// <summary>baseline 実行が終わった</summary>
    /// <param name="Ms">baseline の所要時間</param>
    /// <param name="Tests">発見したテストの件数</param>
    /// <param name="FailingExcluded">baseline で失敗し対象から外したテストの件数</param>
    public sealed record BaselineCompleted(double Ms, int Tests, int FailingExcluded) : RunProgress;

    /// <summary>実行計画が確定した</summary>
    /// <param name="Planned">テスト実行にかける変異の件数</param>
    /// <param name="NoCoverage">被覆がなく即確定した変異の件数</param>
    /// <param name="ExcludedStatic">static 除外で対象外にした変異の件数</param>
    public sealed record ExecutionPlanned(int Planned, int NoCoverage, int ExcludedStatic) : RunProgress;

    /// <summary>前回の保存から判定を継承した</summary>
    /// <param name="Count">継承した判定の件数</param>
    public sealed record VerdictsInherited(int Count) : RunProgress;

    /// <summary>変異のテスト実行を始めた</summary>
    /// <param name="SharedCount">共有ホストで実行する変異の件数</param>
    /// <param name="IsolatedCount">隔離 process で実行する変異の件数</param>
    /// <param name="Lanes">同時に走らせる lane 数</param>
    public sealed record ExecutionStarted(int SharedCount, int IsolatedCount, int Lanes) : RunProgress;

    /// <summary>判定の確定が進んだ</summary>
    /// <param name="Done">確定した変異の件数</param>
    /// <param name="Total">実行対象の変異の総数</param>
    public sealed record VerdictProgress(int Done, int Total) : RunProgress;
}
