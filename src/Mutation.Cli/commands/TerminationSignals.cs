using System.Runtime.InteropServices;

namespace Mutation.Cli.Commands;

/// <summary>走行を止める信号の受け取りと、その終了コードへの対応</summary>
/// <remarks>最初の要求は走行を取り消して判定を確定した変異を報告へ書き出させ、二度目の要求は既定の動作へ譲って強制的に終える</remarks>
internal sealed class TerminationSignals : IDisposable
{
    /// <summary>走行へ渡す取り消しの要求の元</summary>
    private readonly CancellationTokenSource source = new();

    /// <summary>受け取る信号の登録</summary>
    private readonly PosixSignalRegistration[] registrations;

    /// <summary>最初に受けた信号の終了コード。受けていなければ零</summary>
    private int exitCode;

    /// <summary>終了を求める三つの信号の受け取りの開始</summary>
    public TerminationSignals()
    {
        registrations =
        [
            Register(PosixSignal.SIGHUP, 129),
            Register(PosixSignal.SIGINT, 130),
            Register(PosixSignal.SIGTERM, 143),
        ];
    }

    /// <summary>走行へ渡す取り消しの要求</summary>
    public CancellationToken Token => source.Token;

    /// <summary>最初に受けた信号の、shell の慣例に沿う終了コード(128 に信号番号を足した値)。受けていなければ不在</summary>
    public int? ExitCode
    {
        get
        {
            var code = Volatile.Read(ref exitCode);
            return code == 0 ? null : code;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var registration in registrations)
        {
            registration.Dispose();
        }

        source.Dispose();
    }

    /// <summary>一つの信号の受け取りの登録</summary>
    private PosixSignalRegistration Register(PosixSignal signal, int code) =>
        PosixSignalRegistration.Create(
            signal,
            context =>
            {
                if (Interlocked.CompareExchange(ref exitCode, code, 0) != 0)
                {
                    return;
                }

                context.Cancel = true;
                Console.Error.WriteLine($"{signal} を受けた。判定を確定した変異を報告へ書き出して止まる(もう一度受けると強制的に終える)");
                _ = source.CancelAsync();
            }
        );
}
