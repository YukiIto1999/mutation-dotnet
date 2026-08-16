using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>被覆情報から変異ごとの実行計画を導く純粋な計算</summary>
public static class MutantScheduler
{
    /// <summary>全変異の実行計画と即時確定する結果の算出</summary>
    /// <param name="mutants">検査対象の変異の列</param>
    /// <param name="coverage">初回実行で得た被覆表</param>
    /// <param name="tests">初回実行で観測したテストの素性</param>
    /// <param name="excludeStatic">static 初期化でしか実行されない変異を対象外にするか</param>
    /// <returns>実行が要る変異の計画と、即時確定した変異の対</returns>
    public static SchedulingOutcome Plan(
        IReadOnlyList<Mutant> mutants,
        CoverageMap coverage,
        IReadOnlyList<TestCaseInfo> tests,
        bool excludeStatic
    )
    {
        var tables = new SchedulingTables(mutants, coverage, tests);
        var plans = new List<MutantPlan>();
        var noCoverage = new List<MutantId>();
        var excluded = new List<MutantId>();
        foreach (var mutant in mutants)
        {
            var covering = tables.Covering(mutant);
            if (!mutant.InStaticContext && !coverage.IsAmbient(mutant.Id))
            {
                if (covering.Length == 0)
                {
                    noCoverage.Add(mutant.Id);
                    continue;
                }

                plans.Add(new MutantPlan(mutant.Id, new MutantExecution.Resident(covering, tables.Budget(covering))));
                continue;
            }

            if (excludeStatic)
            {
                excluded.Add(mutant.Id);
                continue;
            }

            if (tables.StaticExecution(mutant, covering) is not { } execution)
            {
                noCoverage.Add(mutant.Id);
                continue;
            }

            plans.Add(new MutantPlan(mutant.Id, execution));
        }

        return new SchedulingOutcome(plans, noCoverage, excluded);
    }

    /// <summary>一回の計画で使う表引きの束</summary>
    private sealed class SchedulingTables
    {
        /// <summary>baseline の所要時間に掛ける予算の係数</summary>
        private const double BudgetFactor = 2.0;

        /// <summary>短いテストでも保証する予算の下駄</summary>
        private const double BudgetFloorMs = 2000.0;

        /// <summary>変異と被覆テストの対応表</summary>
        private readonly CoverageMap coverage;

        /// <summary>passing テストの連番から所要時間への対応</summary>
        private readonly Dictionary<TestIndex, double> durationByIndex;

        /// <summary>全 passing テストの所要時間昇順の連番</summary>
        private readonly TestIndex[] allPassingOrdered;

        /// <summary>ファイルごとの、その変異群を被覆する全テストの連番</summary>
        private readonly Dictionary<string, TestIndex[]> testsByFile;

        /// <summary>一回の計画に要る表引きの構築</summary>
        public SchedulingTables(IReadOnlyList<Mutant> mutants, CoverageMap coverage, IReadOnlyList<TestCaseInfo> tests)
        {
            this.coverage = coverage;
            durationByIndex = tests.Where(t => t.BaselinePassed).ToDictionary(t => t.Index, t => t.BaselineMs);
            allPassingOrdered = durationByIndex.OrderBy(p => p.Value).Select(p => p.Key).ToArray();
            testsByFile = mutants
                .GroupBy(m => m.FilePath, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => Ordered(group.SelectMany(m => coverage.CoveringTests(m.Id))),
                    StringComparer.Ordinal
                );
        }

        /// <summary>変異を被覆するテスト連番の所要時間昇順の列</summary>
        public TestIndex[] Covering(Mutant mutant) => Ordered(coverage.CoveringTests(mutant.Id));

        /// <summary>テスト列に応じた実行時間予算</summary>
        public double Budget(IReadOnlyList<TestIndex> order) =>
            (order.Sum(i => durationByIndex[i]) * BudgetFactor) + BudgetFloorMs;

        /// <summary>static 文脈の変異の実行形。走らせるテストがなければ不在</summary>
        public MutantExecution? StaticExecution(Mutant mutant, TestIndex[] covering)
        {
            var isolationOrder = Ordered(coverage.TriggeringTests(mutant.Id))
                .Concat(covering)
                .Concat(testsByFile[mutant.FilePath])
                .Concat(allPassingOrdered)
                .Distinct()
                .ToArray();
            if (isolationOrder.Length == 0)
            {
                return null;
            }

            var isolationBudget = Budget(isolationOrder);
            return covering.Length == 0
                ? new MutantExecution.Isolated(isolationOrder, isolationBudget)
                : new MutantExecution.ResidentThenIsolated(covering, Budget(covering), isolationOrder, isolationBudget);
        }

        /// <summary>passing テストだけを所要時間昇順に並べた列</summary>
        private TestIndex[] Ordered(IEnumerable<TestIndex> testIndexes) =>
            testIndexes.Where(durationByIndex.ContainsKey).Distinct().OrderBy(i => durationByIndex[i]).ToArray();
    }
}
