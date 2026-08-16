

namespace Mutation.Mutating.Domain;

/// <summary>binlog から回収した一 project の csc 呼び出し</summary>
/// <param name="ProjectFile">呼び出し元 project の絶対 path</param>
/// <param name="Arguments">実行 file 名を除いた csc の引数列</param>
/// <param name="OutputPath">/out で指定された出力 assembly の絶対 path</param>
public sealed record CscInvocation(string ProjectFile, IReadOnlyList<string> Arguments, string OutputPath);
