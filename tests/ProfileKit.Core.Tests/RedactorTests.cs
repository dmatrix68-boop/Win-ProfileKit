using System.Text;
using ProfileKit.Core.Catalog;
using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;
using ProfileKit.Core.Text;

namespace ProfileKit.Core.Tests;

public class RedactorTests
{
    private const string DeepLSettings = """
        {
          "UserPreferences": { "CustomShortcut": "Ctrl+C+C", "UserUiTheme": "Dark" },
          "Configuration": {
            "User": {
              "Email": "x@example.org",
              "UserData": "AQAAANCMnd8BFdERjHoAwE",
              "TokenProtected": "AQAAANCMnd8secret1",
              "LastAccessTokenProtected": "AQAAANCMnd8secret2"
            },
            "Session": { "IdentityTokenProtected": "AQAAANCMnd8secret3" }
          }
        }
        """;

    private static RedactRule DeepLRule => new() { File = "settings.json", Json = ["Configuration.User.UserData", "**.*Protected"] };

    [Fact]
    public void Json_paths_and_wildcards_are_removed_rest_kept()
    {
        var removed = 0;
        var result = Redactor.RedactJson(DeepLSettings, DeepLRule.Json, ref removed);
        Assert.Equal(4, removed);
        Assert.DoesNotContain("secret", result);
        Assert.DoesNotContain("UserData", result);
        Assert.Contains("Ctrl+C+C", result);
        Assert.Contains("x@example.org", result);
    }

    [Fact]
    public void Ini_keys_with_and_without_section()
    {
        var ini = "[Generic]\r\nLanguage=de\r\nSMTPUser=me\r\nSMTPPassword=geheim\r\n[Other]\r\nSMTPPassword=auch\r\n; SMTPPassword=kommentar\r\n";
        var removed = 0;
        var all = Redactor.RedactIni(ini, ["SMTPPassword", "SMTPUser"], ref removed);
        Assert.Equal(3, removed);
        Assert.DoesNotContain("geheim", all);
        Assert.DoesNotContain("auch", all);
        Assert.Contains("; SMTPPassword=kommentar", all);
        Assert.Contains("Language=de\r\n", all);

        removed = 0;
        var scoped = Redactor.RedactIni(ini, ["Other.SMTPPassword"], ref removed);
        Assert.Equal(1, removed);
        Assert.Contains("geheim", scoped);
    }

    [Fact]
    public void Utf16_bom_is_preserved_and_binary_or_unmatched_returns_null()
    {
        var enc = new UnicodeEncoding(false, true);
        var bytes = enc.GetPreamble().Concat(enc.GetBytes("[Generic]\r\nSMTPPassword=x\r\nTheme=1\r\n")).ToArray();
        var rule = new RedactRule { File = "aida64.ini", Ini = ["SMTPPassword"] };
        var result = Redactor.Redact(bytes, [rule])!.Value;
        Assert.Equal(1, result.Removed);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, result.Bytes[..2]);
        Assert.Equal("[Generic]\r\nTheme=1\r\n", enc.GetString(result.Bytes, 2, result.Bytes.Length - 2));

        Assert.Throws<RedactionException>(() => Redactor.Redact([1, 0, 2, 0, 0, 0, 3, 0, 9], [rule]));  // binär → fail closed
        Assert.Null(Redactor.Redact(Encoding.UTF8.GetBytes("[A]\nTheme=1\n"), [rule]));
        Assert.Equal(DeepLSettings, Redactor.RedactJson(DeepLSettings, ["Fehlt.Gibts.Nicht"], ref Unused));
    }

    private static int Unused;

    [Fact]
    public void Capture_redacts_the_copy_but_not_the_original()
    {
        using var root = new TempRoot();
        var user = root.User("max");
        var original = PathTokens.Expand(@"%APPDATA%\DeepL_SE\settings.json", user);
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        File.WriteAllText(original, DeepLSettings);

        var rule = RuleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "rules")).Find("deepl")!;
        var package = new ProfilePackage(Path.Combine(root.Path, "p"));
        var entry = new CaptureService().CaptureModule(package, rule, user, new MemoryRegistryBackend()).Entry;

        Assert.Equal(4, entry.RedactedEntries);
        var captured = File.ReadAllText(package.PackagedPathFor("deepl", @"%APPDATA%\DeepL_SE\settings.json"));
        Assert.DoesNotContain("secret", captured);
        Assert.Contains("Ctrl+C+C", captured);
        Assert.Contains("secret1", File.ReadAllText(original));
    }

    [Fact]
    public void Shipped_rules_for_aida64_and_deepl()
    {
        var catalog = RuleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "rules"));
        var aida = catalog.Find("aida64extreme")!;
        Assert.Contains(aida.Files, f => f.Path.EndsWith("pkey.txt") && f.Kind == FileKind.Keyfile);
        Assert.Contains(aida.Redact, r => r.Ini.Contains("SMTPPassword"));
        Assert.NotEmpty(catalog.Find("deepl")!.Redact);
        Assert.Contains("FSIV.db", catalog.Find("faststoneimageviewer")!.Excludes);
    }

    [Fact]
    public void Xml_elements_and_attributes_by_xpath()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<configuration>\r\n  <userSettings>\r\n" +
                  "    <setting name=\"DarkMode\" serializeAs=\"String\"><value>True</value></setting>\r\n" +
                  "    <setting name=\"RecentFindThats\" serializeAs=\"Xml\"><value><ArrayOfString><string>ABCD-1234-LICENSE</string></ArrayOfString></value></setting>\r\n" +
                  "  </userSettings>\r\n  <proxy password=\"geheim\" host=\"p\" />\r\n</configuration>";
        var removed = 0;
        var result = Redactor.RedactXml(xml, ["//setting[@name='RecentFindThats']", "//proxy/@password"], ref removed);
        Assert.Equal(2, removed);
        Assert.DoesNotContain("LICENSE", result);
        Assert.DoesNotContain("geheim", result);
        Assert.Contains("<value>True</value>", result);
        Assert.Contains("host=\"p\"", result);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", result);

        removed = 0;
        Assert.Equal("kein xml", Redactor.RedactXml("kein xml", ["//x"], ref removed));
        Assert.Throws<FormatException>(() => RuleCatalog.Validate(new ModuleRule
        {
            Id = "x", DisplayName = "X", Redact = [new RedactRule { File = "a.config", Xml = ["//[kaputt"] }],
        }));
    }

    [Fact]
    public void Xml_like_file_with_several_root_elements()
    {
        // wie RegistryFinder.config: kein gültiges XML-Dokument, sondern mehrere Wurzelelemente
        var text = "<WindowPlacement x=\"1\" />\r\n<ForceDarkMode>true</ForceDarkMode>\r\n" +
                   "<RecentFindThats><Item>ABCD-EFGH-1234</Item><Item>HKLM\\SOFTWARE</Item></RecentFindThats>\r\n" +
                   "<SearchParams><FindThat>geheim</FindThat><MatchCase>false</MatchCase></SearchParams>\r\n";
        var removed = 0;
        var result = Redactor.RedactXml(text, ["//RecentFindThats", "//SearchParams/FindThat"], ref removed);
        Assert.Equal(2, removed);
        Assert.DoesNotContain("ABCD-EFGH", result);
        Assert.DoesNotContain("geheim", result);
        Assert.Contains("<ForceDarkMode>true</ForceDarkMode>", result);
        Assert.Contains("<MatchCase>false</MatchCase>", result);
        Assert.DoesNotContain("__profilekit", result);
        Assert.StartsWith("<WindowPlacement", result);
    }

    [Fact]
    public void Unreadable_file_with_redact_rule_is_not_captured()
    {
        using var root = new TempRoot();
        var user = root.User("max");
        var dir = PathTokens.Expand(@"%APPDATA%\Tool", user);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ kaputt: \"token\" ");   // kein gültiges JSON
        File.WriteAllText(Path.Combine(dir, "other.ini"), "a=1");
        var rule = new ModuleRule
        {
            Id = "tool", DisplayName = "Tool",
            Files = [new FileRule { Path = @"%APPDATA%\Tool" }],
            Redact = [new RedactRule { File = "settings.json", Json = ["**.Token"] }],
        };
        var package = new ProfilePackage(Path.Combine(root.Path, "p"));
        var result = new CaptureService().CaptureModule(package, rule, user, new MemoryRegistryBackend());

        Assert.Equal(1, result.Entry.FileCount);
        Assert.False(File.Exists(package.PackagedPathFor("tool", @"%APPDATA%\Tool\settings.json")));
        Assert.True(File.Exists(package.PackagedPathFor("tool", @"%APPDATA%\Tool\other.ini")));
        Assert.Contains(result.Warnings, w => w.Contains("settings.json"));
    }

    [Fact]
    public void Redact_rule_without_keys_is_invalid()
    {
        var rule = new ModuleRule { Id = "x", DisplayName = "X", Redact = [new RedactRule { File = "a.ini" }] };
        Assert.Throws<FormatException>(() => RuleCatalog.Validate(rule));
    }
}
