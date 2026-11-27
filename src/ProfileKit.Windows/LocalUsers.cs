using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace ProfileKit.Windows;

/// <summary>Lokale Benutzerkonten anlegen und ihr Profil sofort erzeugen.</summary>
[SupportedOSPlatform("windows")]
public static class LocalUsers
{
    private const string AdministratorsSid = "S-1-5-32-544";

    /// <summary>
    /// Legt ein lokales Konto an (falls nicht vorhanden) und erzeugt das Profil aus dem Default-Profil,
    /// sodass ein Profil ohne vorherige Anmeldung angewendet werden kann.
    /// </summary>
    /// <returns>Profilpfad des Benutzers.</returns>
    public static string Create(string userName, string? password, bool administrator)
    {
        if (!UserProfiles.IsAdministrator())
            throw new UnauthorizedAccessException("Zum Anlegen von Benutzern sind Administratorrechte nötig.");

        var info = new NativeMethods.USER_INFO_1
        {
            usri1_name = userName,
            usri1_password = string.IsNullOrEmpty(password) ? null : password,
            usri1_priv = NativeMethods.USER_PRIV_USER,
            usri1_flags = NativeMethods.UF_SCRIPT | NativeMethods.UF_DONT_EXPIRE_PASSWD
                | (string.IsNullOrEmpty(password) ? NativeMethods.UF_PASSWD_NOTREQD : 0),
        };
        var rc = NativeMethods.NetUserAdd(null, 1, ref info, out _);
        if (rc != NativeMethods.NERR_Success && rc != NativeMethods.NERR_UserExists)
            throw new Win32Exception(rc, $"Benutzer '{userName}' konnte nicht angelegt werden (NetUserAdd {rc}).");

        if (administrator) AddToGroup(userName, AdministratorsSid);

        var sid = ((SecurityIdentifier)new NTAccount(Environment.MachineName, userName).Translate(typeof(SecurityIdentifier))).Value;
        var existing = UserProfiles.List().FirstOrDefault(p => p.Sid == sid);
        if (existing is not null) return existing.ProfilePath;

        var buffer = new StringBuilder(260);
        var hr = NativeMethods.CreateProfile(sid, userName, buffer, (uint)buffer.Capacity);
        const int alreadyExists = unchecked((int)0x800700B7);
        if (hr != 0 && hr != alreadyExists)
            throw new Win32Exception(hr, $"Profil für '{userName}' konnte nicht erzeugt werden (CreateProfile 0x{hr:X8}).");

        return buffer.Length > 0 ? buffer.ToString() : UserProfiles.List().First(p => p.Sid == sid).ProfilePath;
    }

    /// <summary>Fügt den Benutzer einer Gruppe hinzu; der Gruppenname wird über die SID aufgelöst (sprachunabhängig).</summary>
    private static void AddToGroup(string userName, string groupSid)
    {
        var groupAccount = new SecurityIdentifier(groupSid).Translate(typeof(NTAccount)).Value;
        var groupName = groupAccount[(groupAccount.IndexOf('\\') + 1)..];
        var member = new NativeMethods.LOCALGROUP_MEMBERS_INFO_3 { lgrmi3_domainandname = $"{Environment.MachineName}\\{userName}" };
        var rc = NativeMethods.NetLocalGroupAddMembers(null, groupName, 3, ref member, 1);
        if (rc != NativeMethods.NERR_Success && rc != NativeMethods.ERROR_MEMBER_IN_ALIAS)
            throw new Win32Exception(rc, $"'{userName}' konnte nicht zu '{groupName}' hinzugefügt werden.");
    }
}
