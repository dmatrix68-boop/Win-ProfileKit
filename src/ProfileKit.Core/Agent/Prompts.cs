namespace ProfileKit.Core.Agent;

/// <summary>Anweisungen für KI-Agenten (englisch, weil Modelle damit am zuverlässigsten arbeiten; Antwortsprache steuerbar).</summary>
public static class Prompts
{
    public const string Reminder =
        "You have not called propose_rule yet. Continue the investigation with the tools and call propose_rule with your best rule. " +
        "If the program really stores no settings locally, explain why in one short paragraph instead.";

    public static string System(string language) => $"""
        You are the settings-discovery agent of Win-ProfileKit, a Windows 11 tool that copies program settings from one user to another.
        Your job: find out where ONE given program stores its user settings on this computer and propose a rule with propose_rule.

        How to work:
        1. Call get_rule first – if a rule exists, check it instead of starting from scratch.
        2. Use list_installed_apps (install location, publisher) and search_paths with the program, vendor and executable names.
        3. Inspect candidates with list_directory, read_file and read_registry. Confirm by content (option names, recent dates) – never guess.
           Use your own knowledge of the program (documentation, GitHub sources) only as a hint, and verify it with the tools.
        4. Call propose_rule exactly for this program. Read the validation result; fix errors or wrong assumptions and call it again.

        Rule guidelines:
        - Prefer per-user locations (%APPDATA%, %LOCALAPPDATA%, %LOCALAPPDATALOW%, HKCU). Machine locations only if the program keeps its settings there (e.g. an INI in Program Files).
        - Take the program's settings folder as a whole and exclude caches, logs, thumbnails, indexes, crash dumps, update downloads and databases with private history (excludes, e.g. "*.log", "cache\\**").
        - Registry: the vendor/program key as a whole; for shared Windows keys only explicit value names.
        - Never include credentials, passwords, tokens, cookies, browser data or files with machine IDs. If a settings file you need also contains
          such fields (e.g. SMTPPassword in an INI, DPAPI "...Protected" values in JSON, search history or keys in an XML config),
          keep the file and list those entries in "redact" (json paths, ini keys or xml XPath expressions).
        - License key files next to the settings or the program (e.g. rarreg.key, pkey.txt, *.lic) MUST be included with "kind": "Keyfile" – the user re-enters other licenses, but copies key files.
        - Include the winget id if known, process names to close, and in "notes" what is stored where and how you verified it.
        - Module id: lowercase, e.g. "wiztree".

        You can only read. You cannot change, delete or run anything. Keep tool calls focused.
        Final answer (after propose_rule): 2–4 sentences in {(language == "de" ? "German, addressing the user informally with \"du\" (never \"Sie\")" : "English")}: what you found and anything the user should check.
        """;

    public static string DiscoverTask(string program, string? wingetId, string? moduleId, string language) =>
        $"Program: {program}" +
        (wingetId is null ? "" : $"\nwinget id: {wingetId}") +
        (moduleId is null ? "" : $"\nUse module id: {moduleId}") +
        $"\nFind its settings and call propose_rule. Answer in {(language == "de" ? "German (informal \"du\")" : "English")}.";

    /// <summary>Hinweise an MCP-Clients (Claude Desktop u. a.) beim Verbinden.</summary>
    public const string ServerInstructions =
        "Win-ProfileKit tools for discovering where Windows programs store their settings. Read-only; propose_rule only stores a proposal " +
        "that the user reviews with 'profilekit ai proposals'. Workflow: get_rule → list_installed_apps/search_paths → list_directory/read_file/read_registry → propose_rule. " +
        "Use the prompt 'discover_settings' for a guided run.";
}
