

namespace Mutation.Verifying.Domain;

/// <summary>確定結果と Stryker 互換の状態名との相互変換</summary>
public static class VerdictNames
{
    /// <summary>Stryker の mutation-report が使う状態名</summary>
    /// <param name="verdict">変異の確定結果</param>
    /// <returns>互換 schema の status 値</returns>
    public static string For(MutantVerdict verdict) =>
        verdict switch
        {
            MutantVerdict.Killed => "Killed",
            MutantVerdict.Survived => "Survived",
            MutantVerdict.TimedOut => "Timeout",
            MutantVerdict.NoCoverage => "NoCoverage",
            MutantVerdict.CompileError => "CompileError",
            MutantVerdict.Ignored => "Ignored",
        };

    /// <summary>保存された状態名からの確定結果の復元。未知の名前は Ignored への縮退</summary>
    /// <param name="status">保存されていた status 値</param>
    /// <param name="killerTest">Killed のときの検出テスト名</param>
    /// <returns>状態名に対応する確定結果</returns>
    public static MutantVerdict Restore(string status, string? killerTest) =>
        status switch
        {
            "Killed" => new MutantVerdict.Killed(killerTest ?? "(baseline)"),
            "Survived" => new MutantVerdict.Survived(),
            "Timeout" => new MutantVerdict.TimedOut(),
            "NoCoverage" => new MutantVerdict.NoCoverage(),
            "CompileError" => new MutantVerdict.CompileError(),
            _ => new MutantVerdict.Ignored(),
        };
}
