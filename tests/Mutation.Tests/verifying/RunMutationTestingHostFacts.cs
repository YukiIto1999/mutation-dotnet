using Mutation.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>走行が worker の host を一度だけ決め、同一性の捕捉と worker の起動で同じ値を使うことの検査</summary>
/// <remarks>代役の host は読むたびに別の値を返すので、二度読む実装は、捕捉した host と起動した host が食い違う形で現れる</remarks>
public sealed class RunMutationTestingHostFacts
{
    /// <summary>準備が受けた host が、全対象の基準実行と変異の実行の起動情報の host と同じで、host を読んだのが一度だけであること</summary>
    [Test]
    public async Task The_host_given_to_preparation_is_the_host_every_worker_is_launched_with()
    {
        var pipeline = new FakePipeline("First", "Second");

        var run = await pipeline.Workflow.ExecuteAsync(pipeline.Request(), _ => { }, CancellationToken.None);

        await Assert.That(run.IsFailed(out _, out _)).IsFalse();
        var captured = pipeline.Prepared.Single().Host;
        await Assert.That(pipeline.Launches.Count).IsEqualTo(4);
        await Assert.That(pipeline.Launches.All(launch => launch.Host == captured)).IsTrue();
        await Assert.That(pipeline.HostReads).IsEqualTo(1);
    }

}
