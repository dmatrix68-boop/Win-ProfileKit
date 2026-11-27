namespace ProfileKit.Core.Learning;

public enum ChangeKind { Added, Modified, Removed }

public sealed record FileChange(string Path, ChangeKind Kind);

public sealed record RegistryChange(string Key, string Name, ChangeKind Kind)
{
    public string FullName => Key + "\\" + (Name.Length == 0 ? "(Standard)" : Name);
}

/// <summary>Unterschied zwischen zwei Snapshots.</summary>
public sealed record SnapshotDiff(IReadOnlyList<FileChange> Files, IReadOnlyList<RegistryChange> Registry)
{
    public bool IsEmpty => Files.Count == 0 && Registry.Count == 0;

    public static SnapshotDiff Compare(Snapshot before, Snapshot after)
    {
        // Nach dem Laden aus JSON sind die Wörterbücher case-sensitiv – für Windows-Pfade vereinheitlichen
        before = Normalize(before);
        after = Normalize(after);

        var files = new List<FileChange>();
        foreach (var (path, stamp) in after.Files)
        {
            if (!before.Files.TryGetValue(path, out var old)) files.Add(new(path, ChangeKind.Added));
            else if (old != stamp) files.Add(new(path, ChangeKind.Modified));
        }
        files.AddRange(before.Files.Keys.Where(p => !after.Files.ContainsKey(p)).Select(p => new FileChange(p, ChangeKind.Removed)));

        var registry = new List<RegistryChange>();
        foreach (var (key, values) in after.Registry)
        {
            before.Registry.TryGetValue(key, out var oldValues);
            foreach (var (name, fp) in values)
            {
                if (oldValues is null || !oldValues.TryGetValue(name, out var oldFp)) registry.Add(new(key, name, ChangeKind.Added));
                else if (oldFp != fp) registry.Add(new(key, name, ChangeKind.Modified));
            }
        }
        foreach (var (key, values) in before.Registry)
        {
            after.Registry.TryGetValue(key, out var newValues);
            registry.AddRange(values.Keys.Where(n => newValues is null || !newValues.ContainsKey(n)).Select(n => new RegistryChange(key, n, ChangeKind.Removed)));
        }

        return new SnapshotDiff(
            files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            registry.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static Snapshot Normalize(Snapshot s) => s with
    {
        Files = new Dictionary<string, FileStamp>(s.Files, StringComparer.OrdinalIgnoreCase),
        Registry = s.Registry.ToDictionary(
            kv => kv.Key,
            kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase),
    };
}
