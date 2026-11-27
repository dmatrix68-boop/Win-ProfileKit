using System.Runtime.Versioning;
using Microsoft.Win32;
using ProfileKit.Core.Registry;
using CoreRegistryValue = ProfileKit.Core.Registry.RegistryValue;

namespace ProfileKit.Windows;

/// <summary>
/// Registry-Zugriff auf einen Benutzer-Hive (angemeldet oder offline geladen) und HKLM (64-Bit-Ansicht).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryBackend : IRegistryBackend, IDisposable
{
    private const string ClassesPrefix = @"Software\Classes";
    private readonly RegistryKey _userRoot;
    private readonly RegistryKey? _classesRoot;
    private readonly RegistryKey _machineRoot;
    private readonly RegistryKey? _offlineSoftware;
    private readonly bool _ownsUserRoot;

    /// <param name="userRoot">Wurzel für HKCU, z. B. <see cref="Microsoft.Win32.Registry.CurrentUser"/> oder HKU\&lt;Mount&gt;.</param>
    /// <param name="classesRoot">Bei offline geladenen Hives: separat geladene UsrClass.dat für HKCU\Software\Classes.</param>
    /// <param name="offlineSoftware">Bei Offline-Images: geladener SOFTWARE-Hive des Images. HKLM\SOFTWARE\… liest dann
    /// daraus statt vom laufenden System; andere HKLM-Bereiche sind nicht verfügbar.</param>
    public WindowsRegistryBackend(RegistryKey userRoot, RegistryKey? classesRoot = null, bool ownsUserRoot = false, RegistryKey? offlineSoftware = null)
    {
        _userRoot = userRoot;
        _classesRoot = classesRoot;
        _ownsUserRoot = ownsUserRoot;
        _offlineSoftware = offlineSoftware;
        _machineRoot = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    }

    /// <summary>Liest bzw. schreibt ein Offline-Image (HKLM\SOFTWARE aus dem Image)?</summary>
    public bool IsOfflineImage => _offlineSoftware is not null;

    public static WindowsRegistryBackend ForCurrentUser() => new(Microsoft.Win32.Registry.CurrentUser);

    public IReadOnlyList<CoreRegistryValue> Read(string key, IReadOnlyCollection<string> valueNames, bool recurse)
    {
        var normalized = RegistryPath.Normalize(key);
        var result = new List<CoreRegistryValue>();
        using var k = Open(normalized, writable: false);
        if (k is null) return result;

        if (valueNames.Count > 0)
        {
            foreach (var name in valueNames)
            {
                var v = ReadValue(k, normalized, name);
                if (v is not null) result.Add(v);
            }
            return result;
        }
        ReadAll(k, normalized, recurse, result);
        return result;
    }

    public CoreRegistryValue? Get(string key, string name)
    {
        var normalized = RegistryPath.Normalize(key);
        using var k = Open(normalized, writable: false);
        return k is null ? null : ReadValue(k, normalized, name);
    }

    public IReadOnlyList<string> GetSubKeyNames(string key)
    {
        try
        {
            using var k = Open(RegistryPath.Normalize(key), writable: false);
            return k is null ? [] : k.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (System.Security.SecurityException) { return []; }
    }

    public void Write(CoreRegistryValue value)
    {
        var (root, sub) = Resolve(RegistryPath.Normalize(value.Key));
        if (root is null) throw new UnauthorizedAccessException($"Im Offline-Image nicht verfügbar: {value.Key}");
        using var k = root.CreateSubKey(sub, writable: true)
            ?? throw new IOException($"Schlüssel konnte nicht angelegt werden: {value.Key}");
        switch (value.Kind)
        {
            case RegValueKind.String:
                k.SetValue(value.Name, value.Text ?? string.Empty, RegistryValueKind.String);
                break;
            case RegValueKind.ExpandString:
                k.SetValue(value.Name, value.Text ?? string.Empty, RegistryValueKind.ExpandString);
                break;
            case RegValueKind.MultiString:
                k.SetValue(value.Name, (value.Lines ?? []).ToArray(), RegistryValueKind.MultiString);
                break;
            case RegValueKind.DWord:
                k.SetValue(value.Name, unchecked((int)(uint)(value.Number ?? 0)), RegistryValueKind.DWord);
                break;
            case RegValueKind.QWord:
                k.SetValue(value.Name, value.Number ?? 0, RegistryValueKind.QWord);
                break;
            case RegValueKind.Binary:
                k.SetValue(value.Name, value.GetBytes(), RegistryValueKind.Binary);
                break;
            case RegValueKind.None:
                k.SetValue(value.Name, value.GetBytes(), RegistryValueKind.None);
                break;
        }
    }

    private RegistryKey? Open(string normalizedKey, bool writable)
    {
        var (root, sub) = Resolve(normalizedKey);
        if (root is null) return null;
        return root.OpenSubKey(sub, writable);
    }

    private (RegistryKey? Root, string SubKey) Resolve(string normalizedKey)
    {
        var (root, sub) = RegistryPath.Split(normalizedKey);
        if (root == RegistryPath.LocalMachine)
        {
            if (_offlineSoftware is null) return (_machineRoot, sub);
            const string software = "SOFTWARE";
            if (sub.Equals(software, StringComparison.OrdinalIgnoreCase)) return (_offlineSoftware, string.Empty);
            return sub.StartsWith(software + "\\", StringComparison.OrdinalIgnoreCase)
                ? (_offlineSoftware, sub[(software.Length + 1)..])
                : (null, sub); // nur SOFTWARE aus dem Image verfügbar
        }
        if (_classesRoot is not null && (sub.Equals(ClassesPrefix, StringComparison.OrdinalIgnoreCase)
            || sub.StartsWith(ClassesPrefix + "\\", StringComparison.OrdinalIgnoreCase)))
            return (_classesRoot, sub[ClassesPrefix.Length..].TrimStart('\\'));
        return (_userRoot, sub);
    }

    private static void ReadAll(RegistryKey key, string keyPath, bool recurse, List<CoreRegistryValue> result)
    {
        foreach (var name in key.GetValueNames().Order(StringComparer.OrdinalIgnoreCase))
        {
            var v = ReadValue(key, keyPath, name);
            if (v is not null) result.Add(v);
        }
        if (!recurse) return;
        foreach (var subName in key.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var sub = key.OpenSubKey(subName, writable: false);
                if (sub is not null) ReadAll(sub, keyPath + "\\" + subName, recurse, result);
            }
            catch (System.Security.SecurityException) { /* nicht lesbare Unterschlüssel überspringen */ }
        }
    }

    private static CoreRegistryValue? ReadValue(RegistryKey key, string keyPath, string name)
    {
        RegistryValueKind kind;
        try { kind = key.GetValueKind(name); }
        catch (IOException) { return null; } // Wert existiert nicht

        var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return kind switch
        {
            RegistryValueKind.String => CoreRegistryValue.FromString(keyPath, name, data as string ?? string.Empty),
            RegistryValueKind.ExpandString => CoreRegistryValue.FromString(keyPath, name, data as string ?? string.Empty, expand: true),
            RegistryValueKind.MultiString => CoreRegistryValue.FromMulti(keyPath, name, data as string[] ?? []),
            RegistryValueKind.DWord => CoreRegistryValue.FromDWord(keyPath, name, data is int i ? i : 0),
            RegistryValueKind.QWord => CoreRegistryValue.FromQWord(keyPath, name, data is long l ? l : 0),
            RegistryValueKind.Binary => CoreRegistryValue.FromBinary(keyPath, name, data as byte[] ?? []),
            _ => CoreRegistryValue.FromBinary(keyPath, name, data as byte[] ?? [], RegValueKind.None),
        };
    }

    public void Dispose()
    {
        if (_ownsUserRoot) _userRoot.Dispose();
        _classesRoot?.Dispose();
        _offlineSoftware?.Dispose();
        _machineRoot.Dispose();
    }
}
