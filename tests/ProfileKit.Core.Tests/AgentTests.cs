using System.Text.Json;
using System.Text.Json.Nodes;
using ProfileKit.Core.Agent;
using ProfileKit.Core.Catalog;
using ProfileKit.Core.Mcp;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Tests;

public sealed class AgentFixture : IDisposable
{
    public AgentFixture()
    {
        User = Root.User("max");
        Write(PathTokens.Expand(@"%APPDATA%\WizTree\WizTree3.ini", User), "[Options]\nTheme=dark\nLast=" + User.ProfilePath + @"\Downloads");
        Write(PathTokens.Expand(@"%APPDATA%\WizTree\rarreg.key", User), "LICENSE-SECRET");
        Write(PathTokens.Expand(@"%PROGRAMFILES%\WizTree\WizTree64.exe", User), "MZ\0\0binary");
        Registry.Write(RegistryValue.FromString(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WizTree_is1", "DisplayName", "WizTree 4.32"));
        Registry.Write(RegistryValue.FromString(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WizTree_is1", "Publisher", "Antibody Software"));
        Registry.Write(RegistryValue.FromString(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WizTree_is1", "InstallLocation", PathTokens.Expand(@"%PROGRAMFILES%\WizTree\", User)));
        Registry.Write(RegistryValue.FromString(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hidden", "DisplayName", "Hidden runtime"));
        Registry.Write(RegistryValue.FromDWord(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hidden", "SystemComponent", 1));
        Registry.Write(RegistryValue.FromDWord(@"HKCU\Software\Antibody Software\WizTree", "WindowWidth", 1200));
        Registry.Write(RegistryValue.FromString(@"HKCU\Software\Antibody Software\WizTree", "LicensePassword", "geheim"));
        Context = new AgentContext
        {
            User = User,
            Registry = Registry,
            Catalog = RuleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "rules")),
            Proposals = new ProposalStore(Path.Combine(Root.Path, "proposals")),
            AgentName = "test",
        };
        Tools = AgentTools.Create(Context).ToDictionary(t => t.Name);
    }

    public TempRoot Root { get; } = new();
    public UserContext User { get; }
    public MemoryRegistryBackend Registry { get; } = new();
    public AgentContext Context { get; }
    public Dictionary<string, AgentTool> Tools { get; }

    public JsonElement Call(string tool, object args)
    {
        var result = Tools[tool].Invoke(JsonSerializer.SerializeToNode(args) as JsonObject);
        Assert.False(result.IsError, result.Text);
        return JsonDocument.Parse(result.Text).RootElement;
    }

    public ToolResult CallRaw(string tool, object args) => Tools[tool].Invoke(JsonSerializer.SerializeToNode(args) as JsonObject);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public static object WizTreeRule(string path = @"%APPDATA%\WizTree") => new
    {
        rule = new
        {
            id = "wiztree",
            displayName = "WizTree",
            wingetId = "AntibodySoftware.WizTree",
            files = new object[] { new { path }, new { path = @"%APPDATA%\WizTree\rarreg.key", kind = "Keyfile" } },
            registry = new object[] { new { key = @"HKCU\Software\Antibody Software\WizTree" } },
            excludes = new[] { "*.log" },
            notes = "WizTree3.ini in %APPDATA%\\WizTree, window size in HKCU.",
        },
    };

    public void Dispose() => Root.Dispose();
}

public class AccessGuardTests : IClassFixture<AgentFixture>
{
    private readonly AgentFixture _f;
    public AccessGuardTests(AgentFixture f) => _f = f;

    [Fact]
    public void Accepts_tokenized_and_absolute_paths_inside_roots()
    {
        var (tok, abs) = _f.Context.Guard.ResolvePath(@"%APPDATA%\WizTree");
        Assert.Equal(@"%APPDATA%\WizTree", tok);
        Assert.Equal(@"%APPDATA%\WizTree", _f.Context.Guard.ResolvePath(abs).Tokenized);
    }

    [Theory]
    [InlineData(@"/etc/passwd")]
    [InlineData(@"C:\Windows\System32\config\SAM")]
    [InlineData(@"%APPDATA%\..\..\..\x")]
    public void Rejects_paths_outside_roots(string path) =>
        Assert.ThrowsAny<Exception>(() => _f.Context.Guard.ResolvePath(path));

    [Theory]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet")]
    [InlineData(@"HKCU\Environment")]
    public void Rejects_registry_outside_roots(string key) =>
        Assert.Throws<UnauthorizedAccessException>(() => AccessGuard.NormalizeRegistryKey(key));

    [Theory]
    [InlineData(@"%APPDATA%\WizTree\rarreg.key", false)]
    [InlineData(@"%APPDATA%\Microsoft\Protect\S-1-5\key", false)]
    [InlineData(@"%USERPROFILE%\.ssh\id_ed25519", false)]
    [InlineData(@"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Login Data", false)]
    [InlineData(@"%APPDATA%\GHISLER\wcx_ftp.ini", false)]
    [InlineData(@"%PROGRAMFILES%\FinalWire\AIDA64 Extreme\pkey.txt", false)]
    [InlineData(@"%APPDATA%\WizTree\WizTree3.ini", true)]
    public void Sensitive_content_is_withheld(string path, bool allowed) =>
        Assert.Equal(allowed, AccessGuard.IsContentAllowed(path));
}

public class AgentToolTests : IClassFixture<AgentFixture>
{
    private readonly AgentFixture _f;
    public AgentToolTests(AgentFixture f) => _f = f;

    [Fact]
    public void Lists_installed_apps_with_tokenized_location_and_hides_system_components()
    {
        var apps = _f.Call("list_installed_apps", new { filter = "wiz" }).GetProperty("apps");
        var app = Assert.Single(apps.EnumerateArray());
        Assert.Equal("WizTree 4.32", app.GetProperty("name").GetString());
        Assert.Equal(@"%PROGRAMFILES%\WizTree", app.GetProperty("installLocation").GetString());
        Assert.Empty(_f.Call("list_installed_apps", new { filter = "Hidden" }).GetProperty("apps").EnumerateArray());
    }

    [Fact]
    public void Search_finds_folders_files_and_registry_keys()
    {
        var r = _f.Call("search_paths", new { query = "wiztree" });
        var files = r.GetProperty("files").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(@"%APPDATA%\WizTree\", files);
        Assert.Contains(@"%PROGRAMFILES%\WizTree\", files);
        Assert.Contains(@"HKCU\Software\Antibody Software\WizTree", r.GetProperty("registryKeys").EnumerateArray().Select(e => e.GetString()));
        Assert.True(_f.CallRaw("search_paths", new { query = "wi" }).IsError);
    }

    [Fact]
    public void Read_file_returns_text_but_withholds_keyfiles_and_binaries()
    {
        var ini = _f.Call("read_file", new { path = @"%APPDATA%\WizTree\WizTree3.ini" });
        Assert.Contains("Theme=dark", ini.GetProperty("content").GetString());

        var key = _f.Call("read_file", new { path = @"%APPDATA%\WizTree\rarreg.key" });
        Assert.False(key.TryGetProperty("content", out _));
        Assert.DoesNotContain("LICENSE-SECRET", key.GetRawText());
        Assert.Contains("withheld", key.GetProperty("note").GetString());

        var exe = _f.Call("read_file", new { path = @"%PROGRAMFILES%\WizTree\WizTree64.exe" });
        Assert.False(exe.TryGetProperty("content", out _));
        Assert.Contains("Binary", exe.GetProperty("note").GetString());
    }

    [Fact]
    public void Read_registry_redacts_sensitive_values()
    {
        var r = _f.Call("read_registry", new { key = @"HKCU\Software\Antibody Software\WizTree" });
        var values = r.GetProperty("values").EnumerateArray().ToDictionary(v => v.GetProperty("name").GetString()!, v => v.GetProperty("data").GetString());
        Assert.Equal("1200", values["WindowWidth"]);
        Assert.Equal("[redacted]", values["LicensePassword"]);
        Assert.True(_f.CallRaw("read_registry", new { key = @"HKLM\SYSTEM\Setup" }).IsError);
    }

    [Fact]
    public void List_directory_and_get_rule()
    {
        var dir = _f.Call("list_directory", new { path = @"%APPDATA%\WizTree" });
        Assert.Equal(2, dir.GetProperty("entries").GetArrayLength());
        Assert.Equal("7-Zip", _f.Call("get_rule", new { winget_id = "7zip.7zip" }).GetProperty("displayName").GetString());
        Assert.True(_f.Call("get_rule", new { }).GetProperty("modules").GetArrayLength() > 10);
    }

    [Fact]
    public void Propose_rule_validates_stores_and_can_be_accepted()
    {
        var bad = _f.Call("propose_rule", new { rule = new { id = "wiztree", displayName = "WizTree", notes = "x", files = new[] { new { path = @"C:\Windows" } } } });
        Assert.False(bad.GetProperty("stored").GetBoolean());

        var ok = _f.Call("propose_rule", AgentFixture.WizTreeRule());
        Assert.True(ok.GetProperty("stored").GetBoolean());
        Assert.Contains("wiztree", _f.Context.ProposedIds);

        var proposal = _f.Context.Proposals.Find("wiztree")!;
        Assert.Equal(Model.RuleOrigin.Ai, proposal.Rule.Origin);
        Assert.Equal("test", proposal.Agent);
        Assert.All(proposal.Validation.Paths, p => Assert.True(p.Exists));
        Assert.True(proposal.Rule.ContainsKeyfiles);

        var rulesDir = Path.Combine(_f.Root.Path, "rules-user");
        var file = _f.Context.Proposals.Accept("wiztree", rulesDir);
        Assert.True(File.Exists(file));
        Assert.Null(_f.Context.Proposals.Find("wiztree"));
        Assert.Equal("WizTree", RuleCatalog.Load(rulesDir).Find("wiztree")!.DisplayName);
    }

    [Fact]
    public void Proposal_with_only_missing_paths_is_rejected()
    {
        var r = _f.Call("propose_rule", new { rule = new { id = "ghost", displayName = "Ghost", notes = "n", files = new[] { new { path = @"%APPDATA%\Ghost" } } } });
        Assert.False(r.GetProperty("stored").GetBoolean());
    }
}

public class McpServerTests : IClassFixture<AgentFixture>
{
    private readonly McpServer _server;
    public McpServerTests(AgentFixture f) => _server = new McpServer(AgentTools.Create(f.Context), "1.2.3");

    private JsonObject Send(object message) => _server.Handle(JsonSerializer.Serialize(message))!;

    [Fact]
    public void Initialize_negotiates_protocol_version()
    {
        var r = Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test" } } });
        Assert.Equal("2025-06-18", r["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("1.2.3", r["result"]!["serverInfo"]!["version"]!.GetValue<string>());
        var unknown = Send(new { jsonrpc = "2.0", id = 2, method = "initialize", @params = new { protocolVersion = "1999-01-01" } });
        Assert.Equal(McpServer.SupportedProtocolVersions[0], unknown["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public void Lists_and_calls_tools()
    {
        var list = Send(new { jsonrpc = "2.0", id = "a", method = "tools/list" });
        var names = list["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("propose_rule", names);
        Assert.Equal(7, names.Count);
        Assert.Equal("a", list["id"]!.GetValue<string>());

        var call = Send(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "read_file", arguments = new { path = @"%APPDATA%\WizTree\WizTree3.ini" } } });
        Assert.False(call["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("Theme=dark", call["result"]!["content"]![0]!["text"]!.GetValue<string>());

        var denied = Send(new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "read_file", arguments = new { path = "/etc/passwd" } } });
        Assert.True(denied["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void Prompts_errors_and_notifications()
    {
        var prompt = Send(new { jsonrpc = "2.0", id = 5, method = "prompts/get", @params = new { name = "discover_settings", arguments = new { program = "WizTree" } } });
        Assert.Contains("WizTree", prompt["result"]!["messages"]![0]!["content"]!["text"]!.GetValue<string>());

        Assert.Equal(-32601, Send(new { jsonrpc = "2.0", id = 6, method = "nope" })["error"]!["code"]!.GetValue<int>());
        Assert.Null(_server.Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Equal(-32700, _server.Handle("{kaputt")!["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32602, Send(new { jsonrpc = "2.0", id = 7, method = "tools/call", @params = new { name = "rm_rf" } })["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Runs_over_streams_line_by_line()
    {
        var input = new StringReader(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""" + "\n" +
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n" +
            """{"jsonrpc":"2.0","id":2,"method":"ping"}""" + "\n");
        var output = new StringWriter();
        await _server.RunAsync(input, output, CancellationToken.None);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"id\":2", lines[1]);
    }
}

public class AgentRunnerTests : IClassFixture<AgentFixture>
{
    private readonly AgentFixture _f;
    public AgentRunnerTests(AgentFixture f) => _f = f;

    private sealed class ScriptedClient(params ChatResponse[] responses) : IChatClient
    {
        private int _i;
        public string Name => "scripted";
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];
        public Task<ChatResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<AgentTool> tools, CancellationToken ct)
        {
            Requests.Add(messages.ToList());
            return Task.FromResult(responses[Math.Min(_i++, responses.Length - 1)]);
        }
    }

    [Fact]
    public async Task Runs_tools_until_final_answer_and_reminds_once()
    {
        var context = new AgentContext { User = _f.User, Registry = _f.Registry, Catalog = _f.Context.Catalog, Proposals = new ProposalStore(Path.Combine(_f.Root.Path, "p2")) };
        var client = new ScriptedClient(
            new ChatResponse("Ich glaube, fertig.", []),                                             // ohne Vorschlag → Erinnerung
            new ChatResponse(null, [new ToolCall("c1", "search_paths", """{"query":"WizTree"}"""), new ToolCall("c2", "nicht_da", "{}")]),
            new ChatResponse(null, [new ToolCall("c3", "propose_rule", JsonSerializer.Serialize(AgentFixture.WizTreeRule()))]),
            new ChatResponse("WizTree speichert in WizTree3.ini.", []));
        var calls = new List<string>();
        var runner = new AgentRunner(client, AgentTools.Create(context), context) { OnToolCall = (n, _, err) => calls.Add(n + (err ? "!" : "")) };

        var result = await runner.RunAsync(Prompts.System("de"), Prompts.DiscoverTask("WizTree", null, "wiztree", "de"), CancellationToken.None);

        Assert.Equal("WizTree speichert in WizTree3.ini.", result.Summary);
        Assert.Equal(["wiztree"], result.ProposedIds);
        Assert.Equal(["search_paths", "nicht_da!", "propose_rule"], calls);
        Assert.False(result.HitStepLimit);
        Assert.Contains(client.Requests[1], m => m.Content == Prompts.Reminder);
        var toolReplies = client.Requests[2].Where(m => m.Role == "tool").ToList();
        Assert.Equal(["c1", "c2"], toolReplies.Select(m => m.ToolCallId));
        Assert.StartsWith("ERROR:", toolReplies[1].Content);
    }

    [Fact]
    public async Task Stops_at_step_limit()
    {
        var context = new AgentContext { User = _f.User, Registry = _f.Registry, Catalog = _f.Context.Catalog, Proposals = new ProposalStore(Path.Combine(_f.Root.Path, "p3")) };
        var client = new ScriptedClient(new ChatResponse(null, [new ToolCall("x", "get_rule", "{}")]));
        var result = await new AgentRunner(client, AgentTools.Create(context), context) { MaxSteps = 3 }.RunAsync("s", "t", CancellationToken.None);
        Assert.True(result.HitStepLimit);
        Assert.Equal(3, client.Requests.Count);
    }
}

public class AiPlumbingTests
{
    private static readonly IReadOnlyList<AgentTool> NoTools = [];

    [Fact]
    public void Builds_openai_request_with_tools_and_tool_messages()
    {
        var tool = new AgentTool("get_rule", "desc", new JsonObject { ["type"] = "object" }, _ => new ToolResult("ok"));
        var req = OpenAiCompatibleChatClient.BuildRequest("m", [
            ChatMessage.System("s"), ChatMessage.User("u"),
            new ChatMessage("assistant", null, [new ToolCall("c1", "get_rule", "{}")]),
            ChatMessage.Tool("c1", "result")], [tool]);
        Assert.Equal("m", req["model"]!.GetValue<string>());
        Assert.Equal("get_rule", req["tools"]![0]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("c1", req["messages"]![2]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("c1", req["messages"]![3]!["tool_call_id"]!.GetValue<string>());
    }

    [Fact]
    public void Parses_tool_calls_with_string_or_object_arguments()
    {
        var openai = OpenAiCompatibleChatClient.ParseResponse("""
            {"choices":[{"finish_reason":"tool_calls","message":{"content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"%APPDATA%\\\\X\"}"}}]}}]}
            """);
        Assert.Equal("read_file", openai.ToolCalls[0].Name);
        Assert.Contains("%APPDATA%", openai.ToolCalls[0].Arguments);

        var ollama = OpenAiCompatibleChatClient.ParseResponse("""
            {"choices":[{"message":{"content":"","tool_calls":[{"function":{"name":"get_rule","arguments":{"id":"x"}}}]}}]}
            """);
        Assert.Equal("call_0", ollama.ToolCalls[0].Id);
        Assert.Equal("""{"id":"x"}""", ollama.ToolCalls[0].Arguments);

        Assert.Throws<InvalidOperationException>(() => OpenAiCompatibleChatClient.ParseResponse("""{"error":{"message":"No endpoints found"}}"""));
    }

    [Fact]
    public void Parses_model_lists()
    {
        var or = ModelCatalog.ParseOpenRouter("""
            {"data":[{"id":"a/tools","name":"A","supported_parameters":["tools","temperature"],"pricing":{"prompt":"0.000001"}},
                     {"id":"b/plain","name":"B","supported_parameters":["temperature"]}]}
            """);
        Assert.True(or.Single(m => m.Id == "a/tools").SupportsTools);
        Assert.False(or.Single(m => m.Id == "b/plain").SupportsTools);
        Assert.Equal(["qwen3:8b", "llama3.1:latest"], ModelCatalog.ParseOllama("""{"models":[{"name":"qwen3:8b"},{"name":"llama3.1:latest"}]}""").Select(m => m.Id));
    }

    [Fact]
    public void Provider_selection_follows_tier_order()
    {
        var s = new AiSettings();
        Assert.Equal(AiProviders.ClaudeCode, ProviderSelector.Choose(s, new() { ClaudeCodePath = "claude.exe", OllamaModels = ["qwen3:8b"] }, null, null).Provider);

        var ollama = ProviderSelector.Choose(s, new() { OllamaModels = ["qwen3:8b"] }, null, null);
        Assert.Equal((AiProviders.Ollama, "qwen3:8b"), (ollama.Provider, ollama.Model));
        Assert.Equal("qwen3:8b", ProviderSelector.Choose(s with { OllamaModel = "qwen3" }, new() { OllamaModels = ["qwen3:8b"] }, null, null).Model);

        Assert.Throws<InvalidOperationException>(() => ProviderSelector.Choose(s, new() { OpenRouterKey = true }, null, null)); // Modell fehlt
        var or = ProviderSelector.Choose(s with { OpenRouterModel = "x/y" }, new() { OpenRouterKey = true }, null, null);
        Assert.Equal((AiProviders.OpenRouter, "x/y"), (or.Provider, or.Model));

        Assert.Throws<InvalidOperationException>(() => ProviderSelector.Choose(s, new() { OllamaModels = ["a"] }, AiProviders.Ollama, "fehlt"));
        Assert.Throws<InvalidOperationException>(() => ProviderSelector.Choose(s, new(), AiProviders.ClaudeCode, null));
        Assert.Throws<InvalidOperationException>(() => ProviderSelector.Choose(s, new(), "gpt", null));
    }

    [Fact]
    public void Mcp_client_config_keeps_other_entries()
    {
        var existing = """{ "mcpServers": { "other": { "command": "x" } }, "globalShortcut": "Ctrl+Space" }""";
        var merged = McpClientConfig.AddServer(existing, @"C:\Tools\profilekit.exe", ["mcp"]);
        var node = JsonNode.Parse(merged)!;
        Assert.Equal("x", node["mcpServers"]!["other"]!["command"]!.GetValue<string>());
        Assert.Equal(@"C:\Tools\profilekit.exe", node["mcpServers"]![McpClientConfig.ServerName]!["command"]!.GetValue<string>());
        Assert.Equal("Ctrl+Space", node["globalShortcut"]!.GetValue<string>());
        Assert.True(McpClientConfig.Contains(merged));
        Assert.False(McpClientConfig.Contains(McpClientConfig.RemoveServer(merged)));
        Assert.True(McpClientConfig.Contains(McpClientConfig.AddServer(null, "p", [])));
    }

    [Theory]
    [InlineData("Total Commander", "totalcommander")]
    [InlineData("Notepad++", "notepadplusplus")]
    [InlineData("XnView MP", "xnviewmp")]
    [InlineData("Größe", "groesse")]
    [InlineData("7-Zip", "7-zip")]
    [InlineData("!!!", "app")]
    public void Module_ids_from_names(string name, string id) => Assert.Equal(id, ModuleIds.FromName(name));
}
