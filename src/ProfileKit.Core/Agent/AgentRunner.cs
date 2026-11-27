using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProfileKit.Core.Agent;

public sealed record AgentResult(string? Summary, int Steps, IReadOnlyList<string> ProposedIds, bool HitStepLimit);

/// <summary>
/// Eigener Agenten-Ablauf für Ollama und OpenRouter: Modell ↔ Werkzeuge, bis das Modell ohne
/// Werkzeugaufruf antwortet oder das Schrittlimit erreicht ist.
/// </summary>
public sealed class AgentRunner(IChatClient client, IReadOnlyList<AgentTool> tools, AgentContext context)
{
    public int MaxSteps { get; init; } = 30;

    /// <summary>Wird bei jedem Werkzeugaufruf gemeldet (für die Konsolenausgabe).</summary>
    public Action<string, string, bool>? OnToolCall { get; init; }

    public async Task<AgentResult> RunAsync(string systemPrompt, string task, CancellationToken ct)
    {
        var messages = new List<ChatMessage> { ChatMessage.System(systemPrompt), ChatMessage.User(task) };
        var byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        string? last = null;

        for (var step = 1; step <= MaxSteps; step++)
        {
            var response = await client.CompleteAsync(messages, tools, ct).ConfigureAwait(false);
            last = response.Content ?? last;
            if (response.ToolCalls.Count == 0)
            {
                // Modell hat aufgehört, ohne einen Vorschlag zu machen → einmal erinnern
                if (context.ProposedIds.Count == 0 && step < MaxSteps && !messages.Any(m => m.Content == Prompts.Reminder))
                {
                    messages.Add(new ChatMessage("assistant", response.Content));
                    messages.Add(ChatMessage.User(Prompts.Reminder));
                    continue;
                }
                return new AgentResult(response.Content, step, context.ProposedIds, HitStepLimit: false);
            }

            messages.Add(new ChatMessage("assistant", response.Content, response.ToolCalls));
            foreach (var call in response.ToolCalls)
            {
                var result = Execute(byName, call);
                OnToolCall?.Invoke(call.Name, call.Arguments, result.IsError);
                messages.Add(ChatMessage.Tool(call.Id, Limit(result.IsError ? "ERROR: " + result.Text : result.Text)));
            }
        }
        return new AgentResult(last, MaxSteps, context.ProposedIds, HitStepLimit: true);
    }

    private static ToolResult Execute(Dictionary<string, AgentTool> byName, ToolCall call)
    {
        if (!byName.TryGetValue(call.Name, out var tool))
            return ToolResult.Error($"Unknown tool '{call.Name}'. Available: {string.Join(", ", byName.Keys)}.");
        JsonObject? args;
        try { args = string.IsNullOrWhiteSpace(call.Arguments) ? new JsonObject() : JsonNode.Parse(call.Arguments) as JsonObject; }
        catch (JsonException ex) { return ToolResult.Error("Arguments are not valid JSON: " + ex.Message); }
        return tool.Invoke(args);
    }

    /// <summary>Werkzeugausgaben begrenzen, damit kleine lokale Modelle nicht überlaufen.</summary>
    private static string Limit(string text) => text.Length <= 60_000 ? text : text[..60_000] + "\n…(truncated)";
}
