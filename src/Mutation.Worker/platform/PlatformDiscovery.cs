using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Mutation.Worker;

/// <summary>Microsoft.Testing.Platform の server mode によるテスト一覧の取得</summary>
public static class PlatformDiscovery
{
    private static readonly TimeSpan AcceptTimeout = TimeSpan.FromSeconds(60);

    /// <summary>実行体を server mode の子 process で起動しての uid 付き一覧の取得</summary>
    /// <param name="testAssembly">テスト assembly の絶対 path</param>
    /// <returns>発見したテストの uid と表示名の列</returns>
    public static IReadOnlyList<DiscoveredTest> List(string testAssembly)
    {
        var host = Environment.ProcessPath ?? throw new InvalidOperationException("dotnet host の path が分からない");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var startInfo = new ProcessStartInfo
            {
                FileName = host,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(testAssembly) ?? ".",
            };
            startInfo.ArgumentList.Add(testAssembly);
            startInfo.ArgumentList.Add("--server");
            startInfo.ArgumentList.Add("--client-port");
            startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
            startInfo.Environment["TESTINGPLATFORM_TELEMETRY_OPTOUT"] = "1";
            using var process =
                Process.Start(startInfo) ?? throw new InvalidOperationException("テスト実行体を起動できない");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                using var client = Accept(listener);
                using var stream = client.GetStream();
                stream.ReadTimeout = (int)AcceptTimeout.TotalMilliseconds;
                return Discover(stream);
            }
            finally
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 既に終了している競合は無害
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>接続待ちの受理。時間内に来なければ失敗</summary>
    private static TcpClient Accept(TcpListener listener)
    {
        var deadline = DateTime.UtcNow + AcceptTimeout;
        while (!listener.Pending())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException("テスト実行体が server mode で接続してこない");
            }

            Thread.Sleep(50);
        }

        return listener.AcceptTcpClient();
    }

    private static List<DiscoveredTest> Discover(NetworkStream stream)
    {
        Send(stream, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":0,
            "clientInfo":{"name":"mutation-dotnet","version":"1.0"},
            "capabilities":{"testing":{"debuggerProvider":false}}}}
            """.ReplaceLineEndings(""));
        WaitResponse(stream, id: 1);
        Send(
            stream,
            $$$"""{"jsonrpc":"2.0","id":2,"method":"testing/discoverTests","params":{"runId":"{{{Guid.NewGuid()}}}"}}"""
        );
        var tests = new List<DiscoveredTest>();
        while (true)
        {
            using var message = ReadMessage(stream);
            var root = message.RootElement;
            if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 2)
            {
                return tests;
            }

            if (root.TryGetProperty("method", out var method) && method.GetString() == "testing/testUpdates/tests")
            {
                Collect(root, tests);
            }
        }
    }

    /// <summary>testUpdates 通知一件からのテスト node の取り込み</summary>
    private static void Collect(JsonElement root, List<DiscoveredTest> tests)
    {
        var changes = root.GetProperty("params").GetProperty("changes");
        if (changes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var change in changes.EnumerateArray())
        {
            var node = change.GetProperty("node");
            var uid = node.GetProperty("uid").GetString() ?? "";
            var name = node.TryGetProperty("display-name", out var displayName) ? displayName.GetString() ?? "" : "";
            if (uid.Length > 0)
            {
                tests.Add(new DiscoveredTest(uid, name));
            }
        }
    }

    /// <summary>指定 id の応答が届くまでの読み流し</summary>
    private static void WaitResponse(NetworkStream stream, int id)
    {
        while (true)
        {
            using var message = ReadMessage(stream);
            if (message.RootElement.TryGetProperty("id", out var got)
                && got.ValueKind == JsonValueKind.Number
                && got.GetInt32() == id)
            {
                return;
            }
        }
    }

    /// <summary>Content-Length 枠付き一 message の送信</summary>
    private static void Send(NetworkStream stream, string body)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(body);
        var header = System.Text.Encoding.ASCII.GetBytes(
            $"Content-Length: {payload.Length}\r\n\r\n"
        );
        stream.Write(header);
        stream.Write(payload);
        stream.Flush();
    }

    /// <summary>Content-Length 枠付き一 message の受信</summary>
    private static JsonDocument ReadMessage(NetworkStream stream)
    {
        var header = new List<byte>();
        while (!EndsWithBlankLine(header))
        {
            var read = stream.ReadByte();
            if (read < 0)
            {
                throw new InvalidOperationException("server mode の接続が途中で閉じた");
            }

            header.Add((byte)read);
        }

        var text = System.Text.Encoding.ASCII.GetString(header.ToArray());
        var lengthLine = text
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .First(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var length = int.Parse(lengthLine.Split(':')[1].Trim(), CultureInfo.InvariantCulture);
        var body = new byte[length];
        stream.ReadExactly(body);
        return JsonDocument.Parse(body);
    }

    /// <summary>header 部の終端(空行)に達したかの判定</summary>
    private static bool EndsWithBlankLine(List<byte> header) =>
        header.Count >= 4
        && header[^4] == (byte)'\r'
        && header[^3] == (byte)'\n'
        && header[^2] == (byte)'\r'
        && header[^1] == (byte)'\n';
}

/// <summary>発見したテスト一件の素性</summary>
/// <param name="Uid">実行体が受け付けるテストの一意識別子</param>
/// <param name="DisplayName">テストの表示名</param>
public sealed record DiscoveredTest(string Uid, string DisplayName);
