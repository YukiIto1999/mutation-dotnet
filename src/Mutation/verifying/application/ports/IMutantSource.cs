using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Verifying.Application;

/// <summary>検査対象の変異一式の提供を担う port。準備と生成の二段で、間に前回結果の照合を挟める</summary>
public interface IMutantSource
{
    /// <summary>対象の build と特定、および生成入力の指紋の算出</summary>
    /// <param name="target">対象とテストの project の指定</param>
    /// <param name="workDirectory">build の中間物を置く directory</param>
    /// <param name="fingerprintSettings">指紋へ畳む設定の要約。指紋が不要なら不在</param>
    /// <returns>成功なら準備済みの対象、失敗なら理由</returns>
    Task<Result<PreparedTarget, PipelineFailure>> PrepareAsync(
        TargetRequest target,
        string workDirectory,
        string? fingerprintSettings
    );

    /// <summary>選別に沿った変異一式の生成。PrepareAsync の成功後にのみ呼べる</summary>
    /// <param name="selection">変異対象の選別の指定</param>
    /// <param name="mutatedDirectory">変異 assembly を書き出す directory</param>
    /// <returns>成功なら変異一式、失敗なら診断付きの失敗</returns>
    Result<GeneratedMutants, PipelineFailure> Generate(SelectionRequest selection, string mutatedDirectory);
}

/// <summary>対象とテストの project の指定</summary>
/// <param name="ProjectPath">変異対象 project の csproj の絶対 path</param>
/// <param name="TestProjectPath">テスト project の csproj の絶対 path</param>
/// <param name="Configuration">build 構成の名前</param>
public sealed record TargetRequest(string ProjectPath, string TestProjectPath, string Configuration);

/// <summary>準備済みの対象の素性</summary>
/// <param name="TestAssemblyPath">テスト assembly の絶対 path</param>
/// <param name="TargetAssemblyName">差し替え対象 assembly の単純名</param>
/// <param name="Fingerprint">生成入力の指紋。要求しなかったときは不在</param>
public sealed record PreparedTarget(string TestAssemblyPath, string TargetAssemblyName, string? Fingerprint);

/// <summary>変異対象の選別の指定</summary>
/// <param name="MutatePatterns">対象に含めるファイルの glob。先頭 `!` は除外。空なら全ファイル</param>
/// <param name="SinceRef">差分運用の基点になる git の参照。使わないなら不在</param>
/// <param name="IgnoredOperators">除外する変異演算子の名前の列</param>
/// <param name="IgnoredMethods">その呼び出しの中を変異させない method 名の列</param>
/// <param name="ProjectPath">--since の基点解決に使う対象 project の絶対 path</param>
public sealed record SelectionRequest(
    IReadOnlyList<string> MutatePatterns,
    string? SinceRef,
    IReadOnlyList<string> IgnoredOperators,
    IReadOnlyList<string> IgnoredMethods,
    string ProjectPath
);

/// <summary>生成された変異一式</summary>
/// <param name="Mutants">採番済みの全変異</param>
/// <param name="CompileErrorIds">rollback で無効化された変異の連番</param>
/// <param name="MutatedAssemblyPath">書き出した変異 assembly の絶対 path</param>
/// <param name="MutateMs">候補収集と採番の所要時間</param>
/// <param name="CompileMs">書換と emit と rollback の所要時間</param>
public sealed record GeneratedMutants(
    IReadOnlyList<Mutant> Mutants,
    IReadOnlySet<MutantId> CompileErrorIds,
    string MutatedAssemblyPath,
    double MutateMs,
    double CompileMs
);
