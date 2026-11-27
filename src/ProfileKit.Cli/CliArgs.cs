namespace ProfileKit.Cli;

/// <summary>Einfacher Parser: Positionsargumente, <c>--option wert</c>, <c>--option=wert</c> und Schalter.</summary>
public sealed class CliArgs
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "current-user", "default-user", "install", "dry-run", "no-backup", "installed", "admin",
        "password-stdin", "verbose", "no-machine", "zip", "help", "machine", "force",
    };

    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positional { get; } = [];

    public static CliArgs Parse(IEnumerable<string> args)
    {
        var result = new CliArgs();
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a is "-h" or "/?") { result._options["help"] = null; continue; }
            if (!a.StartsWith("--", StringComparison.Ordinal)) { result.Positional.Add(a); continue; }

            var name = a[2..];
            string? value = null;
            var eq = name.IndexOf('=');
            if (eq >= 0) { value = name[(eq + 1)..]; name = name[..eq]; }
            else if (!Flags.Contains(name))
            {
                if (i + 1 >= list.Count) throw new CliException($"--{name}: Wert fehlt / value missing.");
                value = list[++i];
            }
            result._options[name] = value;
        }
        return result;
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.GetValueOrDefault(name);

    public string Require(string name) =>
        Get(name) is { Length: > 0 } v ? v : throw new CliException(Core.Text.Localizer.T("missing-option", "--" + name));
}

public sealed class CliException(string message) : Exception(message);
