using Mutation.Verifying.Domain;

namespace Mutation.Verifying.Application;

/// <summary>確定結果の報告書き出しを担う port</summary>
public interface IVerificationReports
{
    /// <summary>互換レポートと実測の一括の書き出し</summary>
    /// <param name="result">実行全体の確定結果</param>
    /// <param name="projectRoot">互換レポート内の相対 path の基準</param>
    /// <param name="reportsDirectory">報告を置く directory</param>
    void Write(MutationRunResult result, string projectRoot, string reportsDirectory);
}
