
namespace Mutation.Mutating.Domain;

/// <summary>変異対象とテストの project の特定</summary>
/// <param name="ProjectPath">変異対象 project の csproj の絶対 path</param>
/// <param name="TestProjectPath">テスト project の csproj の絶対 path</param>
/// <param name="Configuration">build 構成の名前</param>
public sealed record TargetProjects(string ProjectPath, string TestProjectPath, string Configuration);
