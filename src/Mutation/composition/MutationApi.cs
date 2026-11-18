using Mutation.Application;

namespace Mutation.Composition;

/// <summary>組み立て済みの変異検査と変更行判定の公開入口</summary>
/// <param name="Run">変異検査</param>
/// <param name="ChangedLines">保存した報告に対する変更行の判定</param>
public sealed record MutationApi(RunMutationTesting Run, JudgeChangedLines ChangedLines);
