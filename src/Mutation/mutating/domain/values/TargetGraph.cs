using Mutation.Shared;

namespace Mutation.Mutating.Domain;

/// <summary>build graph が示す project 一件の、変更の影響を判定する入力</summary>
/// <param name="ProjectFile">project file の絶対 path</param>
/// <param name="Sources">csc が読むソースの絶対 path</param>
/// <param name="OtherInputs">ソース以外で、内容が生成物へ届く file の絶対 path。additional file、analyzer config、参照する assembly、analyzer と generator、resource、署名の鍵</param>
public sealed record ProjectInputs(string ProjectFile, IReadOnlyList<string> Sources, IReadOnlyList<string> OtherInputs)
{
    /// <summary>project file のある directory</summary>
    public string ProjectDirectory => Path.GetDirectoryName(ProjectFile) ?? "";
}

/// <summary>対象とテストの project と、それらが参照する project の入力</summary>
/// <param name="Target">変異対象 project</param>
/// <param name="Test">対にしたテスト project</param>
/// <param name="Upstream">対象かテストが参照する、build に含まれた他の project</param>
/// <param name="UnbuiltProjects">参照先だが compiler 呼出しが binlog に記録されていない project の絶対 path</param>
public sealed record TargetGraph(
    ProjectInputs Target,
    ProjectInputs Test,
    IReadOnlyList<ProjectInputs> Upstream,
    IReadOnlyList<string> UnbuiltProjects
);

/// <summary>binlog から特定した対象一件</summary>
/// <param name="Sut">対象 project の csc 呼び出し</param>
/// <param name="TestAssembly">テスト assembly の実行時 path</param>
/// <param name="Graph">対象とテストと、それらが参照する project の入力</param>
public sealed record LocatedTarget(CscInvocation Sut, string TestAssembly, TargetGraph Graph);

/// <summary>変更の影響から導いた、変異候補の範囲の選択</summary>
/// <param name="Selection">報告に残す選択の記録</param>
/// <param name="Files">変異させるファイルの絶対 path の集合。対象の全範囲なら不在</param>
public sealed record ImpactAssessment(CandidateSelection Selection, IReadOnlySet<string>? Files)
{
    /// <summary>生成する範囲を見分ける鍵。範囲の名前と、変異させるファイルの並び。全範囲なら全部を表す印</summary>
    public string Key =>
        string.Join('\n', Selection.Scope.Name, Files is null ? "*" : string.Join('\n', Files.Order(StringComparer.Ordinal)));
}
