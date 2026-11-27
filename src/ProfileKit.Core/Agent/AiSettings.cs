using ProfileKit.Core.Packaging;
using static ProfileKit.Core.Text.Localizer;

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
            throw new InvalidOperationException(T("sel-unknown", provider, string.Join(", ", AiProviders.All)));

        if (provider == AiProviders.Auto)
        {
            if (available.ClaudeCodePath is not null) return new(AiProviders.ClaudeCode, modelOverride, T("sel-reason-claude"));
            if (available.OllamaModels is { Count: > 0 }) return OllamaChoice(settings, available, modelOverride, T("sel-reason-ollama"));
            if (available.OpenRouterKey && (modelOverride ?? settings.OpenRouterModel) is not null)
                return OpenRouterChoice(settings, available, modelOverride, T("sel-reason-openrouter"));
            throw new InvalidOperationException(T("sel-none"));
        }

        return provider switch
        {
            AiProviders.ClaudeCode => available.ClaudeCodePath is not null
                ? new(provider, modelOverride, T("sel-reason-selected"))
                : throw new InvalidOperationException(T("sel-no-claude")),
            AiProviders.Ollama => OllamaChoice(settings, available, modelOverride, T("sel-reason-selected")),
            _ => OpenRouterChoice(settings, available, modelOverride, T("sel-reason-selected")),
        };
    }

    private static ProviderChoice OllamaChoice(AiSettings s, AiAvailability a, string? model, string reason)
    {
        if (a.OllamaModels is null) throw new InvalidOperationException(T("sel-ollama-off", s.OllamaUrl ?? OpenAiCompatibleChatClient.OllamaDefaultUrl));
        var chosen = model ?? s.OllamaModel ?? a.OllamaModels.FirstOrDefault()
            ?? throw new InvalidOperationException(T("sel-ollama-empty"));
        // "qwen3" passt auf das installierte "qwen3:8b" – Ollama braucht den vollständigen Namen
        var installed = a.OllamaModels.FirstOrDefault(m => m.Equals(chosen, StringComparison.OrdinalIgnoreCase))
            ?? a.OllamaModels.FirstOrDefault(m => m.StartsWith(chosen + ":", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(T("sel-ollama-missing", chosen, string.Join(", ", a.OllamaModels)));
        return new(AiProviders.Ollama, installed, reason);
    }

    private static ProviderChoice OpenRouterChoice(AiSettings s, AiAvailability a, string? model, string reason)
    {
        if (!a.OpenRouterKey) throw new InvalidOperationException(T("sel-no-key"));
        var chosen = model ?? s.OpenRouterModel
            ?? throw new InvalidOperationException(T("sel-no-model"));
        return new(AiProviders.OpenRouter, chosen, reason);
    }
}
