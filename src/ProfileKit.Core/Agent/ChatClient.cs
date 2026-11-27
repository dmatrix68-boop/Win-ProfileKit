using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProfileKit.Core.Agent;

public sealed record ToolCall(string Id, string Name, string Arguments);

public sealed record ChatMessage(string Role, string? Content, IReadOnlyList<ToolCall>? ToolCalls = null, string? ToolCallId = null)
{
    public static ChatMessage System(string text) => new("system", text);
    public static ChatMessage User(string text) => new("user", text);
    public static ChatMessage Tool(string callId, string text) => new("tool", text, ToolCallId: callId);
}

public sealed record ChatResponse(string? Content, IReadOnlyList<ToolCall> ToolCalls, string? FinishReason = null);

/// <summary>Chat-Modell mit Tool-Calling.</summary>
public interface IChatClient
{
    string Name { get; }
    Task<ChatResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<AgentTool> tools, CancellationToken ct);
}

/// <summary>
/// Client für die OpenAI-kompatible Chat-Completions-API. Funktioniert mit Ollama
/// (<c>http://localhost:11434/v1</c>) und OpenRouter (<c>https://openrouter.ai/api/v1</c>).
/// </summary>
public sealed class OpenAiCompatibleChatClient : IChatClient
{
    public const string OllamaDefaultUrl = "http://localhost:11434";
    public const string OpenRouterUrl = "https://openrouter.ai/api/v1";

    private readonly HttpClient _http;
    private readonly Uri _baseUrl;

    public OpenAiCompatibleChatClient(HttpClient http, string baseUrl, string model, string? apiKey, string name)
    {
        _http = http;
        _baseUrl = new Uri(baseUrl.TrimEnd('/') + "/");
        Model = model;
        Name = name;
        ApiKey = apiKey;
    }

    public string Model { get; }
    public string Name { get; }
    private string? ApiKey { get; }

    public static OpenAiCompatibleChatClient ForOllama(HttpClient http, string? url, string model) =>
        new(http, (url ?? OllamaDefaultUrl).TrimEnd('/') + "/v1", model, apiKey: null, name: "ollama:" + model);

    public static OpenAiCompatibleChatClient ForOpenRouter(HttpClient http, string apiKey, string model) =>
        new(http, OpenRouterUrl, model, apiKey, name: "openrouter:" + model);

    public async Task<ChatResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<AgentTool> tools, CancellationToken ct)
    {
        var body = BuildRequest(Model, messages, tools);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, "chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (ApiKey is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Headers.TryAddWithoutValidation("X-Title", "Win-ProfileKit");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{Name}: HTTP {(int)response.StatusCode} – {Shorten(text)}");
        return ParseResponse(text);
    }

    public static JsonObject BuildRequest(string model, IReadOnlyList<ChatMessage> messages, IReadOnlyList<AgentTool> tools)
    {
        var msgs = new JsonArray();
        foreach (var m in messages)
        {
            var o = new JsonObject { ["role"] = m.Role, ["content"] = m.Content };
            if (m.ToolCalls is { Count: > 0 })
            {
                o["tool_calls"] = new JsonArray(m.ToolCalls.Select(c => (JsonNode)new JsonObject
                {
                    ["id"] = c.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
                }).ToArray());
            }
            if (m.ToolCallId is not null) o["tool_call_id"] = m.ToolCallId;
            msgs.Add(o);
        }

        return new JsonObject
        {
            ["model"] = model,
            ["messages"] = msgs,
            ["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.InputSchema.DeepClone(),
                },
            }).ToArray()),
            ["tool_choice"] = "auto",
            ["temperature"] = 0.2,
        };
    }

    public static ChatResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException("Model error: " + (err.TryGetProperty("message", out var msg) ? msg.GetString() : err.ToString()));

        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        var calls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
        {
            var n = 0;
            foreach (var call in tc.EnumerateArray())
            {
                var fn = call.GetProperty("function");
                var args = fn.TryGetProperty("arguments", out var a)
                    ? a.ValueKind == JsonValueKind.String ? a.GetString() ?? "{}" : a.GetRawText()   // Ollama liefert teils ein Objekt
                    : "{}";
                var id = call.TryGetProperty("id", out var idEl) && idEl.GetString() is { Length: > 0 } s ? s : $"call_{n}";
                calls.Add(new ToolCall(id, fn.GetProperty("name").GetString() ?? "", args));
                n++;
            }
        }
        var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        return new ChatResponse(content, calls, finish);
    }

    private static string Shorten(string s) => s.Length <= 400 ? s : s[..400] + "…";
}

/// <summary>Live-Modelllisten – kein Modell ist fest verdrahtet.</summary>
public static class ModelCatalog
{
    public sealed record ModelInfo(string Id, string? Name, bool SupportsTools, string? PromptPrice = null);

    public static async Task<IReadOnlyList<ModelInfo>> OpenRouterAsync(HttpClient http, CancellationToken ct)
    {
        var json = await http.GetStringAsync(OpenAiCompatibleChatClient.OpenRouterUrl + "/models", ct).ConfigureAwait(false);
        return ParseOpenRouter(json);
    }

    public static IReadOnlyList<ModelInfo> ParseOpenRouter(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ModelInfo>();
        foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var tools = m.TryGetProperty("supported_parameters", out var sp) && sp.ValueKind == JsonValueKind.Array
                && sp.EnumerateArray().Any(p => p.GetString() == "tools");
            string? price = m.TryGetProperty("pricing", out var pr) && pr.TryGetProperty("prompt", out var pp) ? pp.GetString() : null;
            list.Add(new ModelInfo(m.GetProperty("id").GetString()!, m.TryGetProperty("name", out var n) ? n.GetString() : null, tools, price));
        }
        return list;
    }

    public static async Task<IReadOnlyList<ModelInfo>> OllamaAsync(HttpClient http, string? url, CancellationToken ct)
    {
        var json = await http.GetStringAsync((url ?? OpenAiCompatibleChatClient.OllamaDefaultUrl).TrimEnd('/') + "/api/tags", ct).ConfigureAwait(false);
        return ParseOllama(json);
    }

    public static IReadOnlyList<ModelInfo> ParseOllama(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("models").EnumerateArray()
            .Select(m => new ModelInfo(m.GetProperty("name").GetString()!, null, SupportsTools: true))
            .ToList();
    }
}
