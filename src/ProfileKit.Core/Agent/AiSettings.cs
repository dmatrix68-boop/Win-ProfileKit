using ProfileKit.Core.Packaging;

namespace ProfileKit.Core.Agent;

/// <summary>KI-Stufen laut SPEC 4.8.</summary>
public static class AiProviders
{
    public const string Auto = "auto";
    public const string ClaudeCode = "claude-code";
    public const string Ollama = "ollama";
    public const string OpenRouter = "openrouter";

    public static readonly string[] All = [Auto, ClaudeCode, Ollama, OpenRouter];
}

/// <summary>KI-Einstellungen (<c>%LOCALAPPDATA%\Win-ProfileKit\ai.json</c>). Der OpenRouter-Key liegt NICHT hier, sondern in der Anmeldeinformationsverwaltung.</summary>
public sealed record AiSettings
{
    public string Provider { get; init; } = AiProviders.Auto;
    public string? OllamaUrl { get; init; }
    public string? OllamaModel { get; init; }
    public string? OpenRouterModel { get; init; }

    /// <summary>Der Benutzer hat bestätigt, dass Pfade und Dateiinhalte an OpenRouter gehen dürfen.</summary>
    public bool OpenRouterConsent { get; init; }

    public int MaxSteps { get; init; } = 30;

    public static string PathFor(string localAppData) => Path.Combine(localAppData, "Win-ProfileKit", "ai.json");

    public static AiSettings Load(string path) => File.Exists(path) ? Json.Read<AiSettings>(path) : new AiSettings();

    public void Save(string path) => Json.Write(path, this);
}

/// <summary>Was auf dem Rechner verfügbar ist.</summary>
public sealed record AiAvailability
{
    public string? ClaudeCodePath { get; init; }
    public IReadOnlyList<string>? OllamaModels { get; init; }   // null = Ollama nicht erreichbar
    public bool OpenRouterKey { get; init; }
}

public sealed record ProviderChoice(string Provider, string? Model, string Reason);

/// <summary>Wählt die KI-Stufe: Agent über die Kommandozeile → Ollama → OpenRouter (SPEC 4.8).</summary>
public static class ProviderSelector
{
    /// <exception cref="InvalidOperationException">Keine Stufe nutzbar; Meldung erklärt, was fehlt.</exception>
    public static ProviderChoice Choose(AiSettings settings, AiAvailability available, string? providerOverride, string? modelOverride)
    {
        var provider = providerOverride ?? settings.Provider;
        if (!AiProviders.All.Contains(provider))
            throw new InvalidOperationException($"Unknown provider '{provider}'. Allowed: {string.Join(", ", AiProviders.All)}.");

        if (provider == AiProviders.Auto)
        {
            if (available.ClaudeCodePath is not null) return new(AiProviders.ClaudeCode, modelOverride, "Claude Code is installed");
            if (available.OllamaModels is { Count: > 0 }) return OllamaChoice(settings, available, modelOverride, "Ollama is running");
            if (available.OpenRouterKey && (modelOverride ?? settings.OpenRouterModel) is not null)
                return OpenRouterChoice(settings, available, modelOverride, "OpenRouter key and model are configured");
            throw new InvalidOperationException(
                "No AI available: install Claude Code, start Ollama with a model, or configure OpenRouter (profilekit ai key set, profilekit ai config --openrouter-model <id>).");
        }

        return provider switch
        {
            AiProviders.ClaudeCode => available.ClaudeCodePath is not null
                ? new(provider, modelOverride, "selected")
                : throw new InvalidOperationException("Claude Code ('claude') was not found in PATH."),
            AiProviders.Ollama => OllamaChoice(settings, available, modelOverride, "selected"),
            _ => OpenRouterChoice(settings, available, modelOverride, "selected"),
        };
    }

    private static ProviderChoice OllamaChoice(AiSettings s, AiAvailability a, string? model, string reason)
    {
        if (a.OllamaModels is null) throw new InvalidOperationException($"Ollama is not reachable at {s.OllamaUrl ?? OpenAiCompatibleChatClient.OllamaDefaultUrl}.");
        var chosen = model ?? s.OllamaModel ?? a.OllamaModels.FirstOrDefault()
            ?? throw new InvalidOperationException("Ollama has no models installed (ollama pull <model>).");
        // "qwen3" passt auf das installierte "qwen3:8b" – Ollama braucht den vollständigen Namen
        var installed = a.OllamaModels.FirstOrDefault(m => m.Equals(chosen, StringComparison.OrdinalIgnoreCase))
            ?? a.OllamaModels.FirstOrDefault(m => m.StartsWith(chosen + ":", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Ollama model '{chosen}' is not installed. Installed: {string.Join(", ", a.OllamaModels)}.");
        return new(AiProviders.Ollama, installed, reason);
    }

    private static ProviderChoice OpenRouterChoice(AiSettings s, AiAvailability a, string? model, string reason)
    {
        if (!a.OpenRouterKey) throw new InvalidOperationException("No OpenRouter API key stored (profilekit ai key set).");
        var chosen = model ?? s.OpenRouterModel
            ?? throw new InvalidOperationException("No OpenRouter model configured – choose one from 'profilekit ai models --provider openrouter' (profilekit ai config --openrouter-model <id>).");
        return new(AiProviders.OpenRouter, chosen, reason);
    }
}
