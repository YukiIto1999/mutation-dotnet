using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Mutation.Worker;
using Mutation.Protocol;

var serializer = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var requestHandle = ArgumentAfter("--request-handle");
var responseHandle = ArgumentAfter("--response-handle");
var resultsDirectory = ArgumentAfter("--results-directory");
using var requests = new AnonymousPipeClientStream(PipeDirection.In, requestHandle);
using var responses = new AnonymousPipeClientStream(PipeDirection.Out, responseHandle);
using var reader = new StreamReader(requests, new UTF8Encoding(false));
using var writer = new StreamWriter(responses, new UTF8Encoding(false)) { AutoFlush = true };
var realOut = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
Console.SetOut(Console.Error);
ITestHost? host = null;
var sequence = 0;

void Respond(WorkerResponse response)
{
    writer.WriteLine(JsonSerializer.Serialize(response, serializer));
    realOut.WriteLine($"{WorkerContract.OutputSentinel} {sequence}");
}

while (await reader.ReadLineAsync() is { } line)
{
    sequence++;
    if (line.Length == 0)
    {
        continue;
    }

    WorkerRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<WorkerRequest>(line, serializer);
    }
    catch (JsonException exception)
    {
        Respond(new WorkerResponse.Failed($"指示を解釈できない: {exception.Message}"));
        continue;
    }

    if (request is null)
    {
        Respond(new WorkerResponse.Failed("指示が空"));
        continue;
    }

    if (request is WorkerRequest.Shutdown)
    {
        Respond(new WorkerResponse.Done());
        break;
    }

    try
    {
        Respond(
            request switch
            {
                WorkerRequest.Init init => Open(init),
                WorkerRequest.Discover => new WorkerResponse.Discovered(Host().Discover()),
                WorkerRequest.Baseline baseline => Host().Baseline(baseline.TestIds),
                WorkerRequest.Run run => Host().Run(run),
                _ => new WorkerResponse.Failed("未知の指示"),
            }
        );
    }
    catch (Exception exception) when (exception is not OutOfMemoryException)
    {
        Respond(new WorkerResponse.Failed(exception.ToString()));
    }
}

host?.Dispose();
return 0;

ITestHost Host() => host ?? throw new InvalidOperationException("init が先に要る");

WorkerResponse Open(WorkerRequest.Init init)
{
    host = HostSelection.Open(init.TestAssembly, init.MutatedDirectory, init.TargetAssemblyName, resultsDirectory);
    return new WorkerResponse.Opened();
}

string ArgumentAfter(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length
        ? args[index + 1]
        : throw new InvalidOperationException($"{name} が指定されていない");
}
