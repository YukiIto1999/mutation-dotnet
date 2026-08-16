using Mutation.Verifying.Domain;
using System.Diagnostics;

namespace Mutation.Verifying.Application;

/// <summary>一回の実行で段階を跨いで共有する文脈</summary>
/// <param name="Request">一回の変異検査の入力</param>
/// <param name="Layout">出力 directory 配下の置き場の取り決め</param>
/// <param name="Fingerprint">変異の生成入力の指紋。baseline を使わないなら不在</param>
/// <param name="BuildMs">初回 build の所要時間</param>
/// <param name="Progress">進行を伝える通知先</param>
/// <param name="Clock">段階と全体の所要時間の計測</param>
public sealed record PipelineSession(
    RunRequest Request,
    RunLayout Layout,
    string? Fingerprint,
    double BuildMs,
    Action<RunProgress> Progress,
    PhaseClock Clock
);

/// <summary>段階と全体の所要時間の計測</summary>
public sealed class PhaseClock
{
    /// <summary>開始からの全体時間の計測</summary>
    private readonly Stopwatch total = Stopwatch.StartNew();

    /// <summary>現在の段階の時間の計測</summary>
    private readonly Stopwatch phase = Stopwatch.StartNew();

    /// <summary>開始からの経過時間</summary>
    public double TotalMs => total.Elapsed.TotalMilliseconds;

    /// <summary>現在の段階の締めと所要時間の取得。次の段階が始まる</summary>
    /// <returns>締めた段階の所要時間</returns>
    public double EndPhase()
    {
        var ms = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        return ms;
    }

    /// <summary>途中の計測の破棄と次の段階の開始</summary>
    public void StartPhase() => phase.Restart();
}
