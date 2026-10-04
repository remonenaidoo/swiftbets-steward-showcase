namespace SwiftBets.Steward.Domain.Status;

/// <summary>A customer-facing incident on the public status page, with its updates newest first.</summary>
public sealed record StatusIncident(
    Guid Id,
    string Title,
    string Impact,
    string Status,
    IReadOnlyList<string> Components,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    string CreatedBy,
    IReadOnlyList<StatusIncidentUpdate> Updates);

public sealed record StatusIncidentUpdate(string Status, string Message, DateTimeOffset At);
