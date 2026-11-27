using ProfileKit.Core.Packaging;

namespace ProfileKit.Core.Learning;

/// <summary>Laufende Lernsitzung: Umfang und Ausgangs-Snapshot, gespeichert zwischen <c>learn start</c> und <c>learn stop</c>.</summary>
public sealed record LearnSession
{
    public required LearnScope Scope { get; init; }
    public required Snapshot Before { get; init; }

    public static string DefaultPath(string localAppData) =>
        Path.Combine(localAppData, "Win-ProfileKit", "learn", "session.json");

    public void Save(string path) => Json.Write(path, this);

    public static LearnSession? TryLoad(string path) => File.Exists(path) ? Json.Read<LearnSession>(path) : null;
}
