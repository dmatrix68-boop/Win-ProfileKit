namespace ProfileKit.Core.Registry;

/// <summary>In-Memory-Registry für Tests und Trockenläufe.</summary>
public sealed class MemoryRegistryBackend : IRegistryBackend
{
    // Schlüssel (normalisiert, case-insensitive) → Wertname (case-insensitive) → Wert
    private readonly Dictionary<string, Dictionary<string, RegistryValue>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<RegistryValue> All => _keys.Values.SelectMany(v => v.Values);

    public IReadOnlyList<RegistryValue> Read(string key, IReadOnlyCollection<string> valueNames, bool recurse)
    {
        var normalized = RegistryPath.Normalize(key);
        var result = new List<RegistryValue>();
        if (valueNames.Count > 0)
        {
            foreach (var name in valueNames)
            {
                var v = Get(normalized, name);
                if (v is not null) result.Add(v);
            }
            return result;
        }

        var prefix = normalized + "\\";
        foreach (var (k, values) in _keys.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var match = string.Equals(k, normalized, StringComparison.OrdinalIgnoreCase)
                || (recurse && k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (match) result.AddRange(values.Values.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase));
        }
        return result;
    }

    public void Write(RegistryValue value)
    {
        var key = RegistryPath.Normalize(value.Key);
        if (!_keys.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
            _keys[key] = values;
        }
        values[value.Name] = value with { Key = key };
    }

    public IReadOnlyList<string> GetSubKeyNames(string key)
    {
        var prefix = RegistryPath.Normalize(key) + "\\";
        return _keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public RegistryValue? Get(string key, string name) =>
        _keys.TryGetValue(RegistryPath.Normalize(key), out var values) && values.TryGetValue(name, out var v) ? v : null;
}
