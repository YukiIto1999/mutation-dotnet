using TypeModeling.Domain;

namespace Mutation.Verifying.Domain;

/// <summary>変異一件をどの実行形で検査するか</summary>
[ClosedUnion]
public abstract record MutantExecution
{
    /// <summary>列挙した派生以外の実行形を塞ぐ基底の構築</summary>
    private MutantExecution()
    {
    }

    /// <summary>共有ホストで走らせるときのテスト列と実行時間予算</summary>
    public abstract (IReadOnlyList<TestIndex> TestOrder, double BudgetMs) ResidentSlice { get; }

    /// <summary>隔離 process で走らせるときのテスト列と実行時間予算</summary>
    public abstract (IReadOnlyList<TestIndex> TestOrder, double BudgetMs) IsolatedSlice { get; }

    /// <summary>隔離 process だけで確定させる実行形への写像</summary>
    /// <returns>隔離面のテスト列と予算を持つ実行形</returns>
    public Isolated ToIsolated()
    {
        var (order, budget) = IsolatedSlice;
        return new Isolated(order, budget);
    }

    /// <summary>常駐の共有ホストで判定を確定させる実行形</summary>
    /// <param name="TestOrder">実行するテスト連番。所要時間の短い順</param>
    /// <param name="BudgetMs">無限走行と判定するまでの実行時間予算</param>
    public sealed record Resident(IReadOnlyList<TestIndex> TestOrder, double BudgetMs) : MutantExecution
    {
        /// <inheritdoc />
        public override (IReadOnlyList<TestIndex> TestOrder, double BudgetMs) ResidentSlice => (TestOrder, BudgetMs);

        /// <inheritdoc />
        public override (IReadOnlyList<TestIndex> TestOrder, double BudgetMs) IsolatedSlice => (TestOrder, BudgetMs);
    }

    /// <summary>環境変数で事前活性化した新規 process で判定を確定させる実行形</summary>
    /// <param name="TestOrder">実行するテスト連番。所要時間の短い順</param>
    /// <param name="BudgetMs">無限走行と判定するまでの実行時間予算</param>
    public sealed record Isolated(IReadOnlyList<TestIndex> TestOrder, double BudgetMs) : MutantExecution
    {
        /// <inheritdoc />
        public override (IReadOnlyList<TestIndex> TestOrder, double BudgetMs) ResidentSlice => (TestOrder, BudgetMs);

        /// <inheritdoc />
        public override (IReadOnlyList<TestIndex> TestOrder, double BudgetMs) IsolatedSlice => (TestOrder, BudgetMs);
    }

    /// <summary>共有ホストで先に試し、生存したときだけ新規 process で確かめる実行形</summary>
    /// <param name="ResidentOrder">共有ホストで実行するテスト連番</param>
    /// <param name="ResidentBudgetMs">共有ホストでの実行時間予算</param>
    /// <param name="IsolatedOrder">生存時に新規 process で実行するテスト連番</param>
    /// <param name="IsolatedBudgetMs">新規 process での実行時間予算</param>
    public sealed record ResidentThenIsolated(
        IReadOnlyList<TestIndex> ResidentOrder,
        double ResidentBudgetMs,
        IReadOnlyList<TestIndex> IsolatedOrder,
        double IsolatedBudgetMs
    ) : MutantExecution
    {
        /// <inheritdoc />
        public override (IReadOnlyList<TestIndex> TestOrder, double BudgetMs) ResidentSlice => (ResidentOrder, ResidentBudgetMs);

        /// <inheritdoc />
        public override (IReadOnlyList<TestIndex> TestOrder, double BudgetMs) IsolatedSlice => (IsolatedOrder, IsolatedBudgetMs);
    }
}
