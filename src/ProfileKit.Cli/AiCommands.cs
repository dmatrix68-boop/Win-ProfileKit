using System.Text;
using System.Text.Json;
using ProfileKit.Core.Agent;
using ProfileKit.Core.Catalog;
using ProfileKit.Core.Mcp;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Winget;
using ProfileKit.Windows;
using static ProfileKit.Core.Text.Localizer;

namespace ProfileKit.Cli;

/// <summary>Befehle <c>mcp …</c> und <c>ai …</c> (SPEC 4.8).</summary>
internal sealed class AiCommands(CliArgs args, Func<RuleCatalog> loadCatalog)
{
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string SettingsPath => AiSettings.PathFor(LocalAppData);
    private static string UserRulesDirectory => Path.Combine(LocalAppData, "Win-ProfileKit", "rules");
    private static ProposalStore Proposals => new(ProposalStore.DefaultDirectory(LocalAppData));
    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "profilekit.exe");
    private static string Version => typeof(AiCommands).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    // ------------------------------------------------------------------- mcp

    /// <summary><c>profilekit mcp</c>: MCP-Server über stdio. stdout gehört ausschließlich dem Protokoll.</summary>
    public int Serve()
    {
        using var user = UserTarget.OpenCurrent();
        var context = CreateContext(user, args.Get("agent") ?? "mcp");
        var log = args.Has("verbose") ? Console.Error : TextWriter.Null;
        var server = new McpServer(AgentTools.Create(context), Version, Language) { Log = log };

        using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        server.RunAsync(stdin, stdout, CancellationToken.None).GetAwaiter().GetResult();
        return ExitCodes.Ok;
    }

    public int Install()
    {
        if (args.Has("uninstall"))
        {
            var removed = AiEnvironment.UninstallFromClaudeDesktop();
            Console.WriteLine(T("mcp-uninstalled", removed.Count == 0 ? "-" : string.Join(", ", removed)));
            return ExitCodes.Ok;
        }
        var files = AiEnvironment.InstallIntoClaudeDesktop(ExePath);
        Console.WriteLine(T("mcp-installed", string.Join(Environment.NewLine + "  ", files)));
        return ExitCodes.Ok;
    }

    public int Config()
    {
        Console.WriteLine(T("mcp-config-hint"));
        Console.WriteLine(McpClientConfig.AddServer(null, ExePath, ["mcp"]));
        return ExitCodes.Ok;
    }

    // -------------------------------------------------------------------- ai

    public int Status()
    {
        var settings = AiSettings.Load(SettingsPath);
        var apps = AiEnvironment.Detect();
        Console.WriteLine(T("ai-status-apps"));
        if (apps.Count == 0) Console.WriteLine(T("ai-status-none"));
        foreach (var app in apps) Console.WriteLine($"  {app.Name,-22} {app.Kind,-8} {app.Integration}");

        var ollama = AiEnvironment.ProbeOllamaAsync(Http, settings.OllamaUrl, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine(T("ai-status-ollama", settings.OllamaUrl ?? OpenAiCompatibleChatClient.OllamaDefaultUrl,
            ollama is null ? T("ai-status-ollama-off") : ollama.Count == 0 ? T("none") : string.Join(", ", ollama)));
        var hasKey = CredentialStore.Read(CredentialStore.OpenRouterTarget) is { Length: > 0 };
        Console.WriteLine(T("ai-status-openrouter", hasKey ? T("yes") : T("no"), settings.OpenRouterModel ?? T("none"),
            settings.OpenRouterConsent ? T("yes") : T("no")));

        try
        {
            var choice = ProviderSelector.Choose(settings, Availability(settings, ollama, hasKey), null, null);
            Console.WriteLine(T("ai-status-choice", choice.Provider + (choice.Model is null ? "" : " · " + choice.Model)));
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine(T("ai-status-nochoice", ex.Message));
        }
        return ExitCodes.Ok;
    }

    public int Models()
    {
        var settings = AiSettings.Load(SettingsPath);
        var provider = args.Get("provider") ?? AiProviders.OpenRouter;
        IReadOnlyList<ModelCatalog.ModelInfo> models = provider switch
        {
            AiProviders.Ollama => ModelCatalog.OllamaAsync(Http, settings.OllamaUrl, CancellationToken.None).GetAwaiter().GetResult(),
            AiProviders.OpenRouter => ModelCatalog.OpenRouterAsync(Http, CancellationToken.None).GetAwaiter().GetResult(),
            _ => throw new CliException("--provider ollama | openrouter"),
        };
        var filter = args.Get("filter");
        var list = models.Where(m => (args.Has("all") || m.SupportsTools)
                                     && (filter is null || m.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                         .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
        Console.WriteLine(T("models-header", list.Count));
        foreach (var m in list)
            Console.WriteLine(m.PromptPrice is null ? $"  {m.Id}" : $"  {m.Id,-55} {PricePerMillion(m.PromptPrice)}");
        return ExitCodes.Ok;
    }

    public int Configure()
    {
        var s = AiSettings.Load(SettingsPath);
        if (args.Get("provider") is { } p)
        {
            if (!AiProviders.All.Contains(p)) throw new CliException("--provider " + string.Join(" | ", AiProviders.All));
            s = s with { Provider = p };
        }
        if (args.Get("ollama-url") is { } url) s = s with { OllamaUrl = url };
        if (args.Get("ollama-model") is { } om) s = s with { OllamaModel = om };
        if (args.Get("openrouter-model") is { } orm) s = s with { OpenRouterModel = orm };
        if (args.Get("max-steps") is { } ms && int.TryParse(ms, out var steps)) s = s with { MaxSteps = Math.Clamp(steps, 5, 100) };
        if (args.Has("accept-cloud")) s = s with { OpenRouterConsent = true };
        if (args.Has("revoke-cloud")) s = s with { OpenRouterConsent = false };
        s.Save(SettingsPath);
        Console.WriteLine(T("ai-settings", SettingsPath));
        Console.WriteLine(JsonSerializer.Serialize(s, Json.Options));
        return ExitCodes.Ok;
    }

    public int Key()
    {
        var action = args.Positional.Count >= 3 ? args.Positional[2] : "set";
        if (action == "remove")
        {
            Console.WriteLine(CredentialStore.Delete(CredentialStore.OpenRouterTarget) ? T("ai-key-removed") : T("ai-key-missing"));
            return ExitCodes.Ok;
        }
        var key = args.Has("stdin") || Console.IsInputRedirected ? Console.In.ReadLine() : ReadHidden(T("ai-key-prompt"));
        if (string.IsNullOrWhiteSpace(key)) throw new CliException(T("ai-key-missing"));
        CredentialStore.Write(CredentialStore.OpenRouterTarget, key.Trim());
        Console.WriteLine(T("ai-key-saved", CredentialStore.OpenRouterTarget));
        return ExitCodes.Ok;
    }

    public int Discover()
    {
        var program = args.Get("app") ?? (args.Positional.Count >= 3 ? args.Positional[2] : null)
            ?? throw new CliException(T("missing-option", "--app <Programm>"));
        var moduleId = args.Get("id") ?? ModuleIds.FromName(program);
        ProfilePackage.ValidateId(moduleId);
        var wingetId = args.Get("winget");

        var settings = AiSettings.Load(SettingsPath);
        var needsOllama = (args.Get("provider") ?? settings.Provider) is AiProviders.Auto or AiProviders.Ollama;
        var ollama = needsOllama ? AiEnvironment.ProbeOllamaAsync(Http, settings.OllamaUrl, CancellationToken.None).GetAwaiter().GetResult() : null;
        var openRouterKey = CredentialStore.Read(CredentialStore.OpenRouterTarget);
        var choice = ProviderSelector.Choose(settings, Availability(settings, ollama, openRouterKey is { Length: > 0 }), args.Get("provider"), args.Get("model"));

        if (choice.Provider == AiProviders.OpenRouter && !settings.OpenRouterConsent)
        {
            if (!args.Has("accept-cloud"))
            {
                Console.Error.WriteLine(T("ai-cloud-warning"));
                return ExitCodes.Partial;
            }
            settings = settings with { OpenRouterConsent = true };
            settings.Save(SettingsPath);
            Console.WriteLine(T("ai-consent-saved"));
        }

        Console.WriteLine(T("ai-choice", choice.Provider, choice.Model is null ? "" : " · " + choice.Model, choice.Reason));
        Console.WriteLine(T("ai-start", program, moduleId));
        var started = DateTimeOffset.UtcNow.AddSeconds(-1);
        var language = Language;

        if (choice.Provider == AiProviders.ClaudeCode)
        {
            var claude = AiEnvironment.FindExecutable("claude")!;
            var prompt = Prompts.System(language) + "\n\n" + Prompts.DiscoverTask(program, wingetId, moduleId, language);
            var (code, output) = new ClaudeCodeRunner(claude, ExePath).Run(prompt, choice.Model);
            Console.WriteLine();
            Console.WriteLine(output.Trim());
            if (code != 0) Console.Error.WriteLine(T("warning", $"claude exit code {code}"));
        }
        else
        {
            using var user = UserTarget.OpenCurrent();
            IChatClient client = choice.Provider == AiProviders.Ollama
                ? OpenAiCompatibleChatClient.ForOllama(Http, settings.OllamaUrl, choice.Model!)
                : OpenAiCompatibleChatClient.ForOpenRouter(Http, openRouterKey!, choice.Model!);
            var context = CreateContext(user, client.Name);
            var runner = new AgentRunner(client, AgentTools.Create(context), context)
            {
                MaxSteps = settings.MaxSteps,
                OnToolCall = (tool, arguments, error) =>
                    Console.WriteLine(T("ai-tool", tool, Shorten(arguments)) + (error ? "  ✗" : "")),
            };
            var result = runner.RunAsync(Prompts.System(language), Prompts.DiscoverTask(program, wingetId, moduleId, language), CancellationToken.None)
                .GetAwaiter().GetResult();
            Console.WriteLine();
            if (!string.IsNullOrWhiteSpace(result.Summary)) Console.WriteLine(result.Summary.Trim());
            if (result.HitStepLimit) Console.Error.WriteLine(T("warning", T("ai-steplimit", result.Steps)));
        }

        var fresh = Proposals.List().Where(p => p.CreatedUtc >= started).ToList();
        Console.WriteLine();
        if (fresh.Count == 0) { Console.WriteLine(T("ai-no-proposal")); return ExitCodes.Partial; }
        foreach (var p in fresh) PrintProposal(p);
        return ExitCodes.Ok;
    }

    public int ProposalsList()
    {
        if (args.Get("show") is { } id)
        {
            var p = Proposals.Find(id) ?? throw new CliException(T("ai-not-found", id));
            PrintProposal(p);
            Console.WriteLine(JsonSerializer.Serialize(p.Rule, Json.Options));
            return ExitCodes.Ok;
        }
        var all = Proposals.List();
        if (all.Count == 0) Console.WriteLine(T("ai-no-proposals"));
        foreach (var p in all) PrintProposal(p, brief: true);
        return ExitCodes.Ok;
    }

    public int Accept()
    {
        var id = Positional(2, "<id>");
        if (loadCatalog().Find(id) is { } existing && !args.Has("force"))
            throw new CliException(T("learn-exists", id, existing.DisplayName));
        Console.WriteLine(T("ai-accepted", Proposals.Accept(id, UserRulesDirectory)));
        return ExitCodes.Ok;
    }

    public int Reject()
    {
        var id = Positional(2, "<id>");
        if (!Proposals.Reject(id)) throw new CliException(T("ai-not-found", id));
        Console.WriteLine(T("ai-rejected", id));
        return ExitCodes.Ok;
    }

    // --------------------------------------------------------------- helpers

    private AgentContext CreateContext(UserTarget user, string agentName)
    {
        IReadOnlyList<InstalledPackage>? cached = null;
        return new AgentContext
        {
            User = user.Context,
            Registry = user.Registry,
            Catalog = loadCatalog(),
            Proposals = Proposals,
            AgentName = agentName,
            WingetInventory = () => cached ??= new WingetClient().ListInstalled(),
        };
    }

    private static AiAvailability Availability(AiSettings settings, IReadOnlyList<string>? ollama, bool openRouterKey) => new()
    {
        ClaudeCodePath = AiEnvironment.FindExecutable("claude"),
        OllamaModels = ollama,
        OpenRouterKey = openRouterKey,
    };

    private static void PrintProposal(Proposal p, bool brief = false)
    {
        Console.WriteLine(T("ai-proposal", p.Rule.Id, p.Agent, p.Validation.Paths.Count, p.Validation.Keys.Count));
        if (brief) return;
        foreach (var path in p.Validation.Paths)
            Console.WriteLine(T("ai-proposal-path", path.Exists ? "✓" : "✗", path.Path, path.Files, FormatBytes(path.Bytes)));
        foreach (var key in p.Validation.Keys)
            Console.WriteLine(T("ai-proposal-key", key.Exists ? "✓" : "✗", key.Key, key.Values));
        foreach (var w in p.Validation.Warnings) Console.WriteLine("    ! " + w);
        if (!string.IsNullOrWhiteSpace(p.Rule.Notes)) Console.WriteLine("    " + p.Rule.Notes);
        Console.WriteLine(T("ai-proposal-next", p.Rule.Id));
    }

    private string Positional(int index, string name) =>
        args.Positional.Count > index ? args.Positional[index] : throw new CliException(T("missing-option", name));

    private static string Shorten(string s)
    {
        var oneLine = s.Replace('\n', ' ').Replace('\r', ' ');
        return oneLine.Length <= 100 ? oneLine : oneLine[..100] + "…";
    }

    private static string PricePerMillion(string perToken) =>
        decimal.TryParse(perToken, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p)
            ? $"${p * 1_000_000m:0.##}/M"
            : perToken;

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / (1024.0 * 1024):0.0} MB",
    };

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            sb.Append(key.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
