namespace Mutation.Worker;

/// <summary>Microsoft.Testing.Platform 上のテスト基盤ごとの追加引数</summary>
/// <param name="CommonArguments">毎回の実行に付ける引数</param>
/// <param name="FailFastArguments">最初の失敗で打ち切るときに付ける引数</param>
public sealed record PlatformFlavor(IReadOnlyList<string> CommonArguments, IReadOnlyList<string> FailFastArguments)
{
    /// <summary>テスト assembly の directory からの基盤の判別</summary>
    /// <param name="testDirectory">テスト assembly の置き場</param>
    /// <returns>基盤に応じた追加引数。判別できなければ追加なし</returns>
    public static PlatformFlavor Detect(string testDirectory)
    {
        if (File.Exists(Path.Combine(testDirectory, "TUnit.Engine.dll")))
        {
            return new PlatformFlavor(["--disable-logo"], ["--fail-fast"]);
        }

        if (File.Exists(Path.Combine(testDirectory, "xunit.v3.core.dll")))
        {
            return new PlatformFlavor([], ["--stop-on-fail"]);
        }

        return new PlatformFlavor([], []);
    }
}
