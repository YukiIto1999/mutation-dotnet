using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>worker process 一つとの一行 JSON のやり取り</summary>
public sealed class WorkerClient : IDisposable
{
    /// <summary>一行 JSON の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);

    /// <summary>worker の process</summary>
    private readonly Process process;

    /// <summary>指示を流す側の匿名パイプ</summary>
    private readonly AnonymousPipeServerStream requestPipe;

    /// <summary>応答を受ける側の匿名パイプ</summary>
    private readonly AnonymousPipeServerStream responsePipe;

    /// <summary>指示の書き込み口</summary>
    private readonly StreamWriter requestWriter;

    /// <summary>応答の読み取り口</summary>
    private readonly StreamReader responseReader;

    /// <summary>実 stdout の回収と目印の待ち合わせ</summary>
    private readonly WorkerOutputBuffer output = new();

    /// <summary>送った指示の連番。目印との突き合わせに使う値</summary>
    private int sequence;

    private WorkerClient(
        Process process,
        AnonymousPipeServerStream requestPipe,
        AnonymousPipeServerStream responsePipe
    )
    {
        this.process = process;
        this.requestPipe = requestPipe;
        this.responsePipe = responsePipe;
        requestWriter = new StreamWriter(requestPipe, new UTF8Encoding(false)) { AutoFlush = true };
        responseReader = new StreamReader(responsePipe, new UTF8Encoding(false));
    }

    /// <summary>process が既に終了しているか</summary>
    public bool HasExited => process.HasExited;

    /// <summary>worker process の起動</summary>
    /// <param name="workerDllPath">worker の assembly の絶対 path</param>
    /// <param name="resultsDirectory">worker 内のテスト実行体が成果物を書く directory</param>
    /// <param name="environment">worker へ渡す追加の環境変数</param>
    /// <returns>起動済みの worker。起動できなければ不在</returns>
    public static WorkerClient? Start(
        string workerDllPath,
        string resultsDirectory,
        IReadOnlyDictionary<string, string>? environment = null
    )
    {
        var requestPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var responsePipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } dotnetHost
                ? dotnetHost
                : Environment.ProcessPath ?? "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(workerDllPath);
        startInfo.ArgumentList.Add("--request-handle");
        startInfo.ArgumentList.Add(requestPipe.GetClientHandleAsString());
        startInfo.ArgumentList.Add("--response-handle");
        startInfo.ArgumentList.Add(responsePipe.GetClientHandleAsString());
        startInfo.ArgumentList.Add("--results-directory");
        startInfo.ArgumentList.Add(resultsDirectory);
        // 短命な run を繰り返す worker では PGO の計装が JIT の固定費を増やす側に働く(cold full-suite 実測 16.7s → 12.8s)
        startInfo.Environment["DOTNET_TieredPGO"] = "0";
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[key] = value;
        }

        var process = Process.Start(startInfo);
        if (process is null)
        {
            requestPipe.Dispose();
            responsePipe.Dispose();
            return null;
        }

        requestPipe.DisposeLocalCopyOfClientHandle();
        responsePipe.DisposeLocalCopyOfClientHandle();
        var client = new WorkerClient(process, requestPipe, responsePipe);
        client.StartDraining();
        return client;
    }

    /// <summary>指示の送信と応答の待機。時間切れなら process を殺した上での不在</summary>
    /// <param name="request">送る指示</param>
    /// <param name="timeout">応答を待つ上限</param>
    /// <returns>応答と、その処理中に worker が実 stdout へ書いた行</returns>
    public async Task<WorkerExchange> SendAsync(WorkerRequest request, TimeSpan timeout)
    {
        var mySequence = Interlocked.Increment(ref sequence);
        try
        {
            await requestWriter.WriteLineAsync(JsonSerializer.Serialize(request, Serializer)).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new WorkerExchange(null, output.Take());
        }

        var readTask = responseReader.ReadLineAsync();
        var finished = await Task.WhenAny(readTask, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != readTask)
        {
            Kill();
            return new WorkerExchange(null, output.Take());
        }

        var line = await readTask.ConfigureAwait(false);
        if (line is null)
        {
            return new WorkerExchange(null, output.Take());
        }

        await Task.WhenAny(output.Completion(mySequence), Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        var response = Parse(line);
        return new WorkerExchange(response, output.Take());
    }

    /// <summary>process の即時停止</summary>
    public void Kill()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // HasExited と Kill の間で process が自然終了する競合は無害
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Kill();
        requestWriter.Dispose();
        responseReader.Dispose();
        requestPipe.Dispose();
        responsePipe.Dispose();
        process.Dispose();
    }

    /// <summary>応答一行の解釈。壊れていれば不在</summary>
    private static WorkerResponse? Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<WorkerResponse>(line, Serializer);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>stdout / stderr の吸い上げの開始</summary>
    private void StartDraining()
    {
        process.OutputDataReceived += (_, e) => output.OnLine(e.Data);
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }
}
