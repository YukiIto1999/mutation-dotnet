using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Application;

/// <summary>対象の再構築と schemata コンパイルを担う port</summary>
public interface IMutationCompilation
{
    /// <summary>csc 呼び出しから変異 assembly 一式への写像</summary>
    /// <param name="sut">対象 project の csc 呼び出し</param>
    /// <param name="mutatedDirectory">変異 assembly を書き出す directory</param>
    /// <param name="policy">どこを変異させるかの選別方針</param>
    /// <returns>成功なら成果一式、失敗なら診断付きの失敗</returns>
    Result<MutatedArtifact, PipelineFailure> Compile(CscInvocation sut, string mutatedDirectory, MutationPolicy policy);
}
