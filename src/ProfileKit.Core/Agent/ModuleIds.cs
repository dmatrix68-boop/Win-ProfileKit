using System.Text;

namespace ProfileKit.Core.Agent;

public static class ModuleIds
{
    /// <summary>Bildet eine Modul-ID aus einem Programmnamen: "Total Commander" → "totalcommander", "Notepad++" → "notepadplusplus".</summary>
    public static string FromName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant().Replace("++", "plusplus").Replace("+", "plus"))
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-') sb.Append(c);
            else if (c is 'ä') sb.Append("ae");
            else if (c is 'ö') sb.Append("oe");
            else if (c is 'ü') sb.Append("ue");
            else if (c is 'ß') sb.Append("ss");
        }
        var id = sb.ToString().Trim('.', '-', '_');
        if (id.Length == 0 || !char.IsLetterOrDigit(id[0])) id = "app" + id;
        return id.Length > 64 ? id[..64] : id;
    }
}
