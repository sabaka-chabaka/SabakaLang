using System.Text;
using System.Text.Json.Nodes;

namespace SabakaLang.LanguageServer;

public sealed class JsonRpc(Stream input, Stream output)
{
    private readonly object _writeLock = new();

    public async Task<JsonObject?> ReadAsync()
    {
        var length = -1;
        while (true)
        {
            var line = await ReadLineAsync();
            if (line is null) return null;
            if (line.Length == 0) break;
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line["Content-Length:".Length..].Trim());
        }
        if (length < 0) return null;
        var buf = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await input.ReadAsync(buf.AsMemory(read, length - read));
            if (n == 0) return null;
            read += n;
        }
        return JsonNode.Parse(buf) as JsonObject;
    }

    private async Task<string?> ReadLineAsync()
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var n = await input.ReadAsync(one.AsMemory(0, 1));
            if (n == 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (one[0] == (byte)'\n') break;
            if (one[0] != (byte)'\r') bytes.Add(one[0]);
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    public void Send(JsonObject message)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_writeLock)
        {
            output.Write(header, 0, header.Length);
            output.Write(body, 0, body.Length);
            output.Flush();
        }
    }

    public void Respond(JsonNode? id, JsonNode? result) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result });

    public void RespondError(JsonNode? id, int code, string message) =>
        Send(new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        });

    public void Notify(string method, JsonNode? @params) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params });
}
