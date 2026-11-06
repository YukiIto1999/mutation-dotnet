
using Mutation.Shared;
using Mutation.Verifying.Domain;
using Mutation.Protocol;
using TypeModeling.Domain;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>baseline 実行による被覆とテスト素性の収集</summary>
public static class BaselineExecution
{
    /// <summary>worker を並べたテスト分担での被覆収集モードの実行</summary>
    /// <param name="launch">worker の起動情報</param>
    /// <param name="concurrency">同時に走らせる worker 数</param>
    /// <returns>成功なら被覆とテストの素性、失敗なら理由</returns>
    public static async Task<Result<BaselineProfile, PipelineFailure>> RunAsync(WorkerLaunchPlan launch, int concurrency)
    {
        var opened = await OpenAsync(launch).ConfigureAwait(false);
        if (opened.IsFailed(out var openFailure, out var openedWorker))
        {
            return openFailure;
        }

        var (first, allIds) = openedWorker;
        using var firstClient = first;
        var lanes = Math.Clamp(concurrency, 1, Math.Max(1, allIds.Count / 8));
        var shards = Shard(allIds, lanes);
        var collections = await Task.WhenAll(
                shards.Select((shard, index) => CollectAsync(launch, index == 0 ? firstClient : null, shard))
            )
            .ConfigureAwait(false);
        var responses = new List<WorkerResponse.Baselined>(collections.Length);
        foreach (var collection in collections)
        {
            if (collection.IsFailed(out var failure, out var baselined))
            {
                return failure;
            }

            responses.Add(baselined);
        }

        var observed = responses.SelectMany(r => r.Tests).ToArray();
        var ambient = responses.SelectMany(r => r.Ambient).ToArray();
        return Result.Succeeded(BaselineMapping.Map(observed, ambient));
    }

    /// <summary>先頭 worker の起動と全テストの一意識別子の発見</summary>
    private static async Task<Result<(WorkerClient, IReadOnlyList<string>), PipelineFailure>> OpenAsync(
        WorkerLaunchPlan launch
    )
    {
        var client = StartCoverageWorker(launch);
        if (client is null)
        {
            return Failure("worker を起動できない");
        }

        var init = await InitAsync(client, launch).ConfigureAwait(false);
        if (init is not null)
        {
            client.Dispose();
            return Result.Failed<PipelineFailure>(init);
        }

        var discovered = await client
            .SendAsync(new WorkerRequest.Discover(), TimeSpan.FromMinutes(5))
            .ConfigureAwait(false);
        if (discovered.Response is WorkerResponse.Discovered { TestIds.Count: > 0 } listed)
        {
            return Result.Succeeded<(WorkerClient, IReadOnlyList<string>)>((client, listed.TestIds));
        }

        client.Dispose();
        return discovered.Response switch
        {
            WorkerResponse.Discovered => Result.Failed<PipelineFailure>(
                new PipelineFailure.NoTestsFound(launch.TestAssemblyPath)
            ),
            WorkerResponse.Failed(var error) => Failure(error),
            _ => Failure("discover に応答がない"),
        };
    }

    /// <summary>一 shard の被覆収集。既存 worker がなければ起動して閉じる</summary>
    private static async Task<Result<WorkerResponse.Baselined, PipelineFailure>> CollectAsync(
        WorkerLaunchPlan launch,
        WorkerClient? existing,
        IReadOnlyList<string> shard
    )
    {
        var client = existing ?? StartCoverageWorker(launch);
        if (client is null)
        {
            return Failure("worker を起動できない");
        }

        try
        {
            if (existing is null && await InitAsync(client, launch).ConfigureAwait(false) is { } initFailure)
            {
                return Result.Failed<PipelineFailure>(initFailure);
            }

            var baseline = await client
                .SendAsync(new WorkerRequest.Baseline(shard), TimeSpan.FromHours(2))
                .ConfigureAwait(false);
            if (baseline.Response is not WorkerResponse.Baselined baselined)
            {
                var reason = baseline.Response is WorkerResponse.Failed(var error) ? error : "baseline に応答がない";
                return Failure(reason);
            }

            await client.SendAsync(new WorkerRequest.Shutdown(), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            return Result.Succeeded(baselined);
        }
        finally
        {
            if (existing is null)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>被覆収集モードの worker の起動。失敗なら不在</summary>
    private static WorkerClient? StartCoverageWorker(WorkerLaunchPlan launch) =>
        WorkerClient.Start(
            launch.WorkerDllPath,
            launch.ResultsDirectory,
            new Dictionary<string, string> { [InjectionContract.CoverageEnvironmentVariable] = "1" }
        );

    /// <summary>init 指示の送信。成功なら不在、失敗なら理由</summary>
    private static async Task<PipelineFailure?> InitAsync(WorkerClient client, WorkerLaunchPlan launch)
    {
        var init = await client.SendAsync(WorkerRequests.Init(launch), TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        return init.Response switch
        {
            WorkerResponse.Opened => null,
            WorkerResponse.Failed(var error) => new PipelineFailure.WorkerFailed(error),
            _ => new PipelineFailure.WorkerFailed("init に応答がない"),
        };
    }

    /// <summary>worker 失敗の case の構築</summary>
    private static Failed<PipelineFailure> Failure(string reason) =>
        Result.Failed<PipelineFailure>(new PipelineFailure.WorkerFailed(reason));

    /// <summary>テストの一意識別子の lane 数への輪番分割</summary>
    private static IReadOnlyList<string>[] Shard(IReadOnlyList<string> ids, int lanes) =>
        ids.Select((id, index) => (id, index))
            .GroupBy(pair => pair.index % lanes)
            .Select(group => (IReadOnlyList<string>)group.Select(pair => pair.id).ToArray())
            .ToArray();
}
