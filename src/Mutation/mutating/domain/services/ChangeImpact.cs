using Mutation.Shared;

namespace Mutation.Mutating.Domain;

/// <summary>基点から変わったファイルが対象の変異候補へ届く関係の判定と、候補の範囲の選択</summary>
/// <remarks>
/// 関係は build graph が示す project の入力から導く。対象のソース、対にしたテストのソース、参照する project のソース、build の入力、project の中のその他の file、消えた file
/// file より細かい関係(変えたソースの未変更の呼び出し元、変えたテストが検出する production)は選べないので、対象の全範囲へ広げる。明示した変更行の部分集合だけは広げず、未分析として残す
/// </remarks>
public static class ChangeImpact
{
    /// <summary>build の設定として、対象とテストの build へ届く file の名前</summary>
    private static readonly HashSet<string> ConfigurationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "global.json",
        "nuget.config",
        "packages.lock.json",
    };

    /// <summary>build の設定として、対象とテストの build へ届く file の拡張子</summary>
    private static readonly HashSet<string> ConfigurationExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".props",
        ".targets",
        ".ruleset",
        ".globalconfig",
        ".editorconfig",
        ".rsp",
    };

    /// <summary>変わったファイルの関係の判定と、変異候補の範囲の選択</summary>
    /// <param name="changes">基点から変わったファイル。消えたファイルを含む</param>
    /// <param name="graph">対象とテストと、それらが参照する project の入力</param>
    /// <param name="changedLinesOnly">変わった行に重なる変異だけを生成する明示した部分集合を求めるか</param>
    /// <returns>候補の範囲と、その理由、未分析の関係</returns>
    public static ImpactAssessment Select(ChangedLines changes, TargetGraph graph, bool changedLinesOnly)
    {
        var classifier = new Classifier(graph);
        var reached = new List<(ImpactRelation Relation, string Path)>();
        var notInputs = new List<string>();
        foreach (var path in changes.Files.Order(StringComparer.Ordinal))
        {
            if (classifier.Classify(path, changes.Deleted.Contains(path)) is { } relation)
            {
                reached.Add((relation, path));
            }
            else
            {
                notInputs.Add(path);
            }
        }

        if (reached.Count == 0)
        {
            return new ImpactAssessment(
                new CandidateSelection(new SelectionScope.NoImpact(), [], [], notInputs),
                new HashSet<string>(StringComparer.Ordinal)
            );
        }

        var production = reached.Where(entry => entry.Relation is ImpactRelation.ProductionChanged).Select(entry => entry.Path).ToArray();
        var reasons = Group(reached);
        return changedLinesOnly
            ? Subset(reasons, production, notInputs)
            : new ImpactAssessment(
                new CandidateSelection(new SelectionScope.FullProduction(), WithCallers(reasons, production), [], notInputs),
                null
            );
    }

    /// <summary>明示した変更行の部分集合。変えたソースだけを候補にし、届くその他の関係は未分析として残す</summary>
    private static ImpactAssessment Subset(ImpactReason[] reasons, string[] production, List<string> notInputs)
    {
        var analysed = reasons.Where(reason => reason.Relation is ImpactRelation.ProductionChanged).ToArray();
        var unanalysed = WithCallers(reasons.Where(reason => reason.Relation is not ImpactRelation.ProductionChanged).ToArray(), production);
        return new ImpactAssessment(
            new CandidateSelection(new SelectionScope.ExplicitSubset(), analysed, unanalysed, notInputs),
            production.ToHashSet(StringComparer.Ordinal)
        );
    }

    /// <summary>関係ごとの file の束ね</summary>
    private static ImpactReason[] Group(List<(ImpactRelation Relation, string Path)> reached) =>
        [
            .. reached
                .GroupBy(entry => entry.Relation)
                .Select(group => new ImpactReason(group.Key, [.. group.Select(entry => entry.Path)])),
        ];

    /// <summary>変えたソースがあるときの、未変更の呼び出し元の関係の追加</summary>
    private static ImpactReason[] WithCallers(ImpactReason[] reasons, string[] production) =>
        production.Length == 0
            ? [.. reasons]
            : [.. reasons, new ImpactReason(new ImpactRelation.CallersOfChangedProduction(), production)];

    /// <summary>build graph の入力から、変わったファイルの関係を判定する表引き</summary>
    private sealed class Classifier
    {
        /// <summary>対象のソース</summary>
        private readonly HashSet<string> productionSources;

        /// <summary>対にしたテストのソース</summary>
        private readonly HashSet<string> testSources;

        /// <summary>参照する project のソース</summary>
        private readonly HashSet<string> upstreamSources;

        /// <summary>ソース以外の build の入力と project file</summary>
        private readonly HashSet<string> buildInputs;

        /// <summary>対象とテストと参照する project の directory</summary>
        private readonly string[] projectDirectories;

        /// <summary>表引きの構築</summary>
        public Classifier(TargetGraph graph)
        {
            var projects = new[] { graph.Target, graph.Test }.Concat(graph.Upstream).ToArray();
            productionSources = graph.Target.Sources.ToHashSet(StringComparer.Ordinal);
            testSources = graph.Test.Sources.ToHashSet(StringComparer.Ordinal);
            upstreamSources = graph.Upstream.SelectMany(project => project.Sources).ToHashSet(StringComparer.Ordinal);
            buildInputs = projects
                .SelectMany(project => project.OtherInputs.Append(project.ProjectFile))
                .ToHashSet(StringComparer.Ordinal);
            projectDirectories = [.. projects.Select(project => project.ProjectDirectory).Where(directory => directory.Length > 0).Distinct(StringComparer.Ordinal)];
        }

        /// <summary>変更の既知の関係と、入力との関係を否定できない変更の分類</summary>
        /// <param name="path">変わったファイルの絶対 path</param>
        /// <param name="deleted">基点にあって消えたファイルか</param>
        /// <returns>届く関係。既知の入力でない build 成果物だけは不在</returns>
        public ImpactRelation? Classify(string path, bool deleted)
        {
            if (productionSources.Contains(path))
            {
                return new ImpactRelation.ProductionChanged();
            }

            if (testSources.Contains(path))
            {
                return new ImpactRelation.TestChanged();
            }

            if (upstreamSources.Contains(path))
            {
                return new ImpactRelation.DependencyChanged();
            }

            if (buildInputs.Contains(path) || IsBuildConfiguration(path))
            {
                return new ImpactRelation.ConfigChanged();
            }
            if (InBuildOutput(path))
            {
                return null;
            }


            return deleted ? new ImpactRelation.FileRemoved() : new ImpactRelation.UnknownInput();
        }

        /// <summary>project の bin と obj の下にある build の成果物か</summary>
        private bool InBuildOutput(string path) =>
            projectDirectories.Any(directory =>
                IsUnder(path, Path.Combine(directory, "bin")) || IsUnder(path, Path.Combine(directory, "obj"))
            );

        /// <summary>対象かテストか参照する project の directory かその祖先に置かれた、build の設定のファイルか</summary>
        private bool IsBuildConfiguration(string path)
        {
            var directory = Path.GetDirectoryName(path);
            var configuration = ConfigurationNames.Contains(Path.GetFileName(path))
                || ConfigurationExtensions.Contains(Path.GetExtension(path));
            return configuration
                && directory is not null
                && projectDirectories.Any(project => project == directory || IsUnder(project, directory));
        }

        /// <summary>path が directory の下にあるか</summary>
        private static bool IsUnder(string path, string directory) =>
            path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
