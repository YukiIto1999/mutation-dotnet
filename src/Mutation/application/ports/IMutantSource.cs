using Mutation.Domain;
using TypeModeling.Domain;

namespace Mutation.Application;

/// <summary>検査対象の変異一式の提供を担う port。準備と生成の二段で、間に前回結果の照合を挟める</summary>
public interface IMutantSource
{
    /// <summary>全対象の build と特定、差分運用の基点からの差分の解決、および再利用の同一性の算出。build は一度だけ</summary>
    /// <remarks>build の失敗は全対象に及ぶ失敗、特定と差分の解決の失敗は対象ごとの失敗。指紋は解決した差分を含む</remarks>
    /// <param name="targets">対象とテストの project の指定の列</param>
    /// <param name="selection">変異対象の選別の指定</param>
    /// <param name="workDirectory">build の中間物と、engine 自身が書く出力を置く directory。テスト project の出力の中にあっても、同一性に入れない</param>
    /// <param name="execution">実行と判定の制御。そのうち判定を左右する設定が同一性に入る。同時実行数や、前回の保存を読むかの指定は入らない</param>
    /// <param name="host">この走行が worker を起動する host。同一性へ畳む worker の実体は、この値が指すものである</param>
    /// <returns>成功なら入力と同じ順の対象ごとの準備の結果、失敗なら全対象に及ぶ理由</returns>
    Task<Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure>> PrepareAsync(
        IReadOnlyList<TargetProjects> targets,
        MutationSelection selection,
        string workDirectory,
        ExecutionSettings execution,
        WorkerHost host
    );
}

/// <summary>準備済みの対象の素性</summary>
/// <param name="Request">元になった対象の指定</param>
/// <param name="TestAssemblyPath">テスト assembly の絶対 path</param>
/// <param name="TargetAssemblyName">差し替え対象 assembly の単純名</param>
/// <param name="Identity">再利用の同一性。入力の閉包の同一性と全体の指紋の対。入力を読めなかったときは不在で、不在なら全体の再構築も変異ごとの継承もしない</param>
/// <param name="Selection">`--since` の実行で、変異候補をどの範囲に選んだかの記録。差分運用でなければ不在</param>
public abstract record PreparedTarget(
    TargetProjects Request,
    string TestAssemblyPath,
    string TargetAssemblyName,
    ReuseIdentity? Identity,
    CandidateSelection? Selection
)
{
    /// <summary>この準備で確定した入力からの変異 assembly の生成</summary>
    /// <param name="mutatedDirectory">変異 assembly を書き出す directory</param>
    /// <returns>成功なら成果一式、失敗なら診断付きの失敗</returns>
    public abstract Result<MutatedArtifact, PipelineFailure> Generate(string mutatedDirectory);
}
