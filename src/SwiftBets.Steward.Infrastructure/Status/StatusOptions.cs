using System.ComponentModel.DataAnnotations;

namespace SwiftBets.Steward.Infrastructure.Status;

/// <summary>The components on the public status page and the readiness URLs that decide each one's state.</summary>
public sealed class StatusOptions
{
    public const string SectionName = "Status";

    [Range(5, 3600)]
    public int PollSeconds { get; set; } = 30;

    public List<StatusComponent> Components { get; set; } = [];
}

public sealed class StatusComponent
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> Urls { get; set; } = [];
}
