using Mutation.Mutating.Domain;

namespace Mutation.Mutating.Application;

/// <summary>生成入力の指紋計算を担う port</summary>
public interface IFingerprints
{
    /// <summary>csc 引数と全ソース内容と設定要約からの指紋の計算</summary>
    /// <param name="invocation">対象 project の csc 呼び出し</param>
    /// <param name="settings">変異の集合と判定に影響する設定の要約</param>
    /// <returns>16 進の指紋。ソース file が読めなければ不在</returns>
    string? Compute(CscInvocation invocation, string settings);
}
