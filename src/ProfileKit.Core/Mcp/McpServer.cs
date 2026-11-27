using System.Text.Json;
using System.Text.Json.Nodes;
using ProfileKit.Core.Agent;

namespace ProfileKit.Core.Mcp;

/// <summary>
/// Minimaler MCP-Server (Model Context Protocol) über stdio: JSON-RPC 2.0, eine Nachricht je Zeile.
/// Unterstützt initialize, ping, tools/list, tools/call, prompts/list, prompts/get.
/// stdout gehört ausschließlich dem Protokoll – Protokollausgaben gehen nach stderr.
/// </summary>
public sealed class McpServer(IReadOnlyList<AgentTool> tools, string serverVersion, string language = "en")
{
    public static readonly string[] SupportedProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    private readonly Dictionary<string, AgentTool> _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public TextWriter Log { get; init; } = TextWriter.Null;

    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break; // Client hat stdin geschlossen
            if (string.IsNullOrWhiteSpace(line)) continue;

            var response = Handle(line);
            if (response is null) continue;
            await output.WriteLineAsync(response.ToJsonString()).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Verarbeitet eine JSON-RPC-Nachricht; null bei Benachrichtigungen.</summary>
    public JsonObject? Handle(string line)
    {
        JsonObject? message;
        try { message = JsonNode.Parse(line) as JsonObject; }
        catch (JsonException) { return Error(null, -32700, "Parse error"); }
        if (message is null) return Error(null, -32600, "Invalid Request");

        var id = message["id"]?.DeepClone();
        var method = message["method"]?.GetValue<string>();
        var @params = message["params"] as JsonObject ?? new JsonObject();

        if (method is null) return id is null ? null : Error(id, -32600, "Invalid Request"); // Antworten an uns ignorieren
        if (id is null)
        {
            Log.WriteLine($"notification: {method}");
            return null; // notifications/initialized, notifications/cancelled …
        }

        try
        {
            return method switch
            {
                "initialize" => Result(id, Initialize(@params)),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, ListTools()),
                "tools/call" => Result(id, CallTool(@params)),
                "prompts/list" => Result(id, ListPrompts()),
                "prompts/get" => Result(id, GetPrompt(@params)),
                "resources/list" => Result(id, new JsonObject { ["resources"] = new JsonArray() }),
                _ => Error(id, -32601, $"Method not found: {method}"),
            };
        }
        catch (McpException ex)
        {
            return Error(id, ex.Code, ex.Message);
        }
    }

    private JsonObject Initialize(JsonObject p)
    {
        var requested = p["protocolVersion"]?.GetValue<string>();
        var version = requested is not null && SupportedProtocolVersions.Contains(requested) ? requested : SupportedProtocolVersions[0];
        Log.WriteLine($"initialize: client={p["clientInfo"]?["name"]} protocol={requested} → {version}");
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
                ["prompts"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject { ["name"] = "win-profilekit", ["title"] = "Win-ProfileKit", ["version"] = serverVersion },
            ["instructions"] = Prompts.ServerInstructions,
        };
    }

    private JsonObject ListTools() => new()
    {
        ["tools"] = new JsonArray(_tools.Values.Select(t => (JsonNode)new JsonObject
        {
            ["name"] = t.Name,
            ["description"] = t.Description,
            ["inputSchema"] = t.InputSchema.DeepClone(),
            ["annotations"] = new JsonObject
            {
                ["readOnlyHint"] = t.Name != "propose_rule",
                ["destructiveHint"] = false,
                ["openWorldHint"] = false,
            },
        }).ToArray()),
    };

    private JsonObject CallTool(JsonObject p)
    {
        var name = p["name"]?.GetValue<string>() ?? throw new McpException(-32602, "Missing tool name");
        if (!_tools.TryGetValue(name, out var tool)) throw new McpException(-32602, $"Unknown tool: {name}");
        var result = tool.Invoke(p["arguments"] as JsonObject);
        Log.WriteLine($"tools/call {name} → {(result.IsError ? "error" : "ok")}");
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
            ["isError"] = result.IsError,
        };
    }

    private static JsonObject ListPrompts() => new()
    {
        ["prompts"] = new JsonArray(new JsonObject
        {
            ["name"] = "discover_settings",
            ["title"] = "Discover program settings",
            ["description"] = "Find where a program stores its settings and propose a Win-ProfileKit rule.",
            ["arguments"] = new JsonArray(
                new JsonObject { ["name"] = "program", ["description"] = "Program name, e.g. WizTree", ["required"] = true },
                new JsonObject { ["name"] = "winget_id", ["description"] = "winget package id (optional)", ["required"] = false },
                new JsonObject { ["name"] = "module_id", ["description"] = "Module id to use (optional)", ["required"] = false }),
        }),
    };

    private JsonObject GetPrompt(JsonObject p)
    {
        if (p["name"]?.GetValue<string>() != "discover_settings") throw new McpException(-32602, "Unknown prompt");
        var args = p["arguments"] as JsonObject ?? new JsonObject();
        var program = args["program"]?.GetValue<string>() ?? throw new McpException(-32602, "Argument 'program' is required");
        var text = Prompts.System(language) + "\n\n" + Prompts.DiscoverTask(program, args["winget_id"]?.GetValue<string>(), args["module_id"]?.GetValue<string>(), language);
        return new JsonObject
        {
            ["description"] = $"Discover settings of {program}",
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject { ["type"] = "text", ["text"] = text },
            }),
        };
    }

    private static JsonObject Result(JsonNode id, JsonObject result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private sealed class McpException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }
}
