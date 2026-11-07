using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Mutation.Protocol;
using Mutation.ScriptedWorker;

// 本物の worker と同じ引数・一行 JSON・実 stdout の目印で往復し、応答だけを台本から返す
var serializer = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var requests = new AnonymousPipeClientStream(PipeDirection.In, ArgumentAfter("--request-handle"));
using var responses = new AnonymousPipeClientStream(PipeDirection.Out, ArgumentAfter("--response-handle"));
using var reader = new StreamReader(requests, new UTF8Encoding(false));
using var writer = new StreamWriter(responses, new UTF8Encoding(false)) { AutoFlush = true };
var behavior = ScriptedBehavior.Load(ArgumentAfter("--results-directory"));
var sequence = 0;
while (await reader.ReadLineAsync() is { } line)
{
    sequence++;
    var request = JsonSerializer.Deserialize<WorkerRequest>(line, serializer);
    var response = await behavior.AnswerAsync(request);
    await writer.WriteLineAsync(JsonSerializer.Serialize(response, serializer));
    await Console.Out.WriteLineAsync($"{WorkerContract.OutputSentinel} {sequence}");
    if (request is WorkerRequest.Shutdown)
    {
        break;
    }
}

return 0;

string ArgumentAfter(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length
        ? args[index + 1]
        : throw new InvalidOperationException($"{name} が指定されていない");
}
