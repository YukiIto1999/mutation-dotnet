

namespace Mutation.Verifying.Infrastructure.Snapshot;

/// <summary>継承元となる一回分の実行の保存形</summary>
/// <param name="TestAssemblyHash">テスト assembly 内容の hash</param>
/// <param name="CompilationFingerprint">変異の生成入力の指紋</param>
/// <param name="MutatedAssemblyHash">変異 assembly 内容の hash</param>
/// <param name="CompileErrorIds">rollback で無効化された変異の連番</param>
/// <param name="Tests">連番順の全テストの素性</param>
/// <param name="AmbientMutants">テスト境界の外か static 初期化区間で観測された変異の連番</param>
/// <param name="Files">ファイルの絶対 path から、その hash と変異列への対応</param>
public sealed record SnapshotDocument(
    string TestAssemblyHash,
    string? CompilationFingerprint,
    string? MutatedAssemblyHash,
    IReadOnlyList<int>? CompileErrorIds,
    IReadOnlyList<SnapshotTest>? Tests,
    IReadOnlyList<int>? AmbientMutants,
    IReadOnlyDictionary<string, SnapshotFile> Files
);

/// <summary>一ファイル分の継承情報</summary>
/// <param name="Hash">ファイル内容の hash</param>
/// <param name="Mutants">連番順の変異の素性と判定</param>
public sealed record SnapshotFile(string Hash, IReadOnlyList<SnapshotMutant> Mutants);

/// <summary>変異一件の素性と前回の判定</summary>
/// <param name="Operator">変異演算子の名前</param>
/// <param name="Line">変異元の 1 始まり行番号</param>
/// <param name="Column">変異元の 1 始まり桁番号</param>
/// <param name="EndLine">変異元終端の 1 始まり行番号</param>
/// <param name="EndColumn">変異元終端の 1 始まり桁番号</param>
/// <param name="Replacement">変異後のコード断片</param>
/// <param name="Status">前回の判定の状態名</param>
/// <param name="KilledBy">検出したテストの表示名。検出以外は不在</param>
/// <param name="Original">変異前のコード断片</param>
/// <param name="InStaticContext">static 初期化文脈にあるか</param>
/// <param name="CoveringTests">被覆したテストの連番。Tests への添字</param>
/// <param name="TriggeringTests">static 初期化を引き起こしたテストの連番。Tests への添字</param>
public sealed record SnapshotMutant(
    string Operator,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Replacement,
    string Status,
    string? KilledBy,
    string? Original = null,
    bool InStaticContext = false,
    IReadOnlyList<int>? CoveringTests = null,
    IReadOnlyList<int>? TriggeringTests = null
);

/// <summary>保存した一テストの素性</summary>
/// <param name="Id">テストの一意識別子</param>
/// <param name="Name">テストの表示名</param>
/// <param name="Ms">実行の所要時間</param>
/// <param name="Passed">成功したか</param>
/// <param name="ProbeCount">実行中の probe 呼び出し回数</param>
public sealed record SnapshotTest(string Id, string Name, double Ms, bool Passed, long ProbeCount);
