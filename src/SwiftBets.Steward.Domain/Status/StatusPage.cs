namespace SwiftBets.Steward.Domain.Status;

/// <summary>Public status page vocabulary: component states, incident impact and incident progress.</summary>
public static class StatusPage
{
    public const string Operational = "operational";
    public const string Degraded = "degraded";
    public const string Outage = "outage";
    public const string Unknown = "unknown";

    public const string Resolved = "resolved";

    public static readonly IReadOnlyList<string> Impacts = ["minor", "major", "critical"];

    public static readonly IReadOnlyList<string> Statuses = ["investigating", "identified", "monitoring", Resolved];

    /// <summary>All checks healthy is operational, some is degraded, none is an outage; no checks yet is unknown.</summary>
    public static string ComponentState(IReadOnlyCollection<bool> checks) =>
        checks.Count == 0 ? Unknown : checks.All(c => c) ? Operational : checks.Any(c => c) ? Degraded : Outage;

    /// <summary>The worst component state; unknown components do not count against the page.</summary>
    public static string Overall(IEnumerable<string> states)
    {
        var known = states.Where(s => s != Unknown).ToList();
        return known.Contains(Outage) ? Outage : known.Contains(Degraded) ? Degraded : Operational;
    }
}
