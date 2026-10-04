using SwiftBets.Steward.Domain.Status;

namespace SwiftBets.Steward.Application.Ports;

/// <summary>Status page incidents and their updates.</summary>
public interface IStatusStore
{
    Task CreateAsync(StatusIncident incident, CancellationToken cancellationToken);

    Task<StatusIncident?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds an update and moves the incident to its status; false when it was already resolved.</summary>
    Task<bool> AddUpdateAsync(Guid id, StatusIncidentUpdate update, string by, CancellationToken cancellationToken);

    /// <summary>Unresolved incidents plus those resolved since the given time, newest first.</summary>
    Task<IReadOnlyList<StatusIncident>> ListAsync(DateTimeOffset since, bool includeAllUnresolved, CancellationToken cancellationToken);
}

/// <summary>The latest health of each status page component.</summary>
public interface IComponentStatus
{
    IReadOnlyList<ComponentHealth> Components { get; }

    DateTimeOffset? UpdatedAt { get; }
}

public sealed record ComponentHealth(string Key, string Name, string State);
