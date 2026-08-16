using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>一指示に対する worker の応答と、その間の実 stdout の出力</summary>
/// <param name="Response">応答。時間切れや process 終了なら不在</param>
/// <param name="Output">処理中に worker が実 stdout へ書いた行。直近数百行に切り詰める</param>
public sealed record WorkerExchange(WorkerResponse? Response, IReadOnlyList<string> Output);
