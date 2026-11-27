using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProfileKit.Core.Mcp;

/// <summary>
/// Trägt den MCP-Server in die Konfiguration eines MCP-Clients ein (Format <c>{"mcpServers": {...}}</c>,
/// z. B. <c>claude_desktop_config.json</c>). Andere Einträge bleiben unverändert.
/// </summary>
public static class McpClientConfig
{
    public const string ServerName = "win-profilekit";

    public static JsonObject ServerEntry(string command, IEnumerable<string> args) => new()
    {
        ["command"] = command,
        ["args"] = new JsonArray(args.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()),
    };

    /// <summary>Fügt den Eintrag hinzu bzw. ersetzt ihn. <paramref name="existingJson"/> darf null/leer sein.</summary>
    public static string AddServer(string? existingJson, string command, IEnumerable<string> args, string name = ServerName)
    {
        var root = Parse(existingJson);
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }
        servers[name] = ServerEntry(command, args);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string RemoveServer(string? existingJson, string name = ServerName)
    {
        var root = Parse(existingJson);
        (root["mcpServers"] as JsonObject)?.Remove(name);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static bool Contains(string? existingJson, string name = ServerName) =>
        Parse(existingJson)["mcpServers"] is JsonObject s && s.ContainsKey(name);

    private static JsonObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new InvalidDataException("The MCP client configuration is not a JSON object.");
    }
}
