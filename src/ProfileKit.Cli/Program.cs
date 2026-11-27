using System.Runtime.Versioning;
using System.Text;
using ProfileKit.Cli;
using ProfileKit.Core.Text;

[assembly: SupportedOSPlatform("windows")]

Console.OutputEncoding = Encoding.UTF8;

CliArgs cli;
try
{
    cli = CliArgs.Parse(args);
}
catch (CliException ex)
{
    Console.Error.WriteLine(Localizer.T("error", ex.Message));
    return ExitCodes.InvalidArguments;
}

Localizer.SetLanguage(cli.Get("lang") ?? Environment.GetEnvironmentVariable("PROFILEKIT_LANG"));

if (cli.Positional.Count == 0 || cli.Has("help") || cli.Positional[0] is "help")
{
    Console.WriteLine(HelpText.Get());
    return cli.Positional.Count == 0 && !cli.Has("help") ? ExitCodes.InvalidArguments : ExitCodes.Ok;
}

try
{
    var commands = new Commands(cli);
    var ai = new AiCommands(cli, commands.LoadCatalog);
    string Sub(int i) => cli.Positional.Count > i ? cli.Positional[i].ToLowerInvariant() : "";
    return cli.Positional[0].ToLowerInvariant() switch
    {
        "users" => commands.Users(),
        "catalog" => commands.Catalog(),
        "inventory" => commands.Inventory(),
        "capture" => commands.Capture(),
        "collect" => commands.Collect(),
        "compare" => commands.Compare(),
        "vhdx" when Sub(1) is "users" => commands.VhdxUsers(),
        "info" => commands.Info(),
        "apply" => commands.Apply(),
        "mcp" => Sub(1) switch
        {
            "" or "serve" => ai.Serve(),
            "install" => ai.Install(),
            "config" => ai.Config(),
            _ => throw new CliException(Localizer.T("unknown-command", "mcp " + Sub(1))),
        },
        "ai" => Sub(1) switch
        {
            "status" or "" => ai.Status(),
            "models" => ai.Models(),
            "config" => ai.Configure(),
            "key" => ai.Key(),
            "discover" => ai.Discover(),
            "proposals" => ai.ProposalsList(),
            "accept" => ai.Accept(),
            "reject" => ai.Reject(),
            _ => throw new CliException(Localizer.T("unknown-command", "ai " + Sub(1))),
        },
        "learn" when cli.Positional.Count >= 2 => cli.Positional[1].ToLowerInvariant() switch
        {
            "start" => commands.LearnStart(),
            "diff" => commands.LearnDiff(),
            "stop" => commands.LearnStop(),
            _ => throw new CliException(Localizer.T("unknown-command", "learn " + cli.Positional[1])),
        },
        "user" when cli.Positional.Count >= 2 && cli.Positional[1] is "create" => commands.CreateUser(),
        _ => throw new CliException(Localizer.T("unknown-command", cli.Positional[0])),
    };
}
catch (CliException ex)
{
    Console.Error.WriteLine(Localizer.T("error", ex.Message));
    Console.Error.WriteLine(Localizer.T("usage"));
    return ExitCodes.InvalidArguments;
}
catch (UnauthorizedAccessException ex)
{
    Console.Error.WriteLine(Localizer.T("error", ex.Message));
    Console.Error.WriteLine(Localizer.T("needs-admin"));
    return ExitCodes.Error;
}
catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or InvalidDataException
                               or FormatException or System.ComponentModel.Win32Exception or TimeoutException
                               or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine(Localizer.T("error", ex.Message));
    if (cli.Has("verbose")) Console.Error.WriteLine(ex);
    return ExitCodes.Error;
}

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int Error = 1;
    public const int Partial = 2;
    public const int InvalidArguments = 3;
}
