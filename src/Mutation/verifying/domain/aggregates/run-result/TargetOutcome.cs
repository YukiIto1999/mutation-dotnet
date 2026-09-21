using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Verifying.Domain;

/// <summary>対象一件の検査の帰結</summary>
[ClosedUnion]
public abstract record TargetOutcome
{
    /// <summary>列挙した派生以外の帰結を塞ぐ基底の構築</summary>
    private TargetOutcome()
    {
    }

    /// <summary>対象 project の名前</summary>
    public abstract string Name { get; }

    /// <summary>判定までの到達</summary>
    public sealed record Completed(TargetResult Result) : TargetOutcome
    {
        /// <inheritdoc />
        public override string Name => Result.Name;
    }

    /// <summary>途中での中断。他の対象の検査は継続</summary>
    /// <param name="Target">対象 project の名前</param>
    /// <param name="Failure">中断の理由</param>
    /// <param name="ElapsedMs">中断までに費やした時間</param>
    public sealed record Failed(string Target, PipelineFailure Failure, double ElapsedMs) : TargetOutcome
    {
        /// <inheritdoc />
        public override string Name => Target;
    }
}
