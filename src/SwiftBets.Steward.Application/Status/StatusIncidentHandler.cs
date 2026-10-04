using SwiftBets.Contracts.Errors;
using SwiftBets.Contracts.Results;
using SwiftBets.Steward.Application.Ports;
using SwiftBets.Steward.Domain.Status;

namespace SwiftBets.Steward.Application.Status;

public sealed record CreateStatusIncident(string? Title, string? Impact, IReadOnlyList<string>? Components, string? Message);

public sealed record AddStatusUpdate(string? Status, string? Message);

/// <summary>Staff open and update public status page incidents; a resolved incident takes no more updates.</summary>
public sealed class StatusIncidentHandler(IStatusStore store, IComponentStatus components, TimeProvider time)
{
    public async Task<Result<StatusIncident>> CreateAsync(CreateStatusIncident request, string by, CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var message = request.Message?.Trim() ?? string.Empty;
        var keys = components.Components.Select(c => c.Key).ToHashSet();
        if (title.Length is 0 or > 120)
        {
            return Error.Validation("invalid_title", "Title must be 1 to 120 characters.");
        }

        if (request.Impact is null || !StatusPage.Impacts.Contains(request.Impact))
        {
            return Error.Validation("invalid_impact", $"Impact must be one of {string.Join(", ", StatusPage.Impacts)}.");
        }

        if (request.Components is not { Count: > 0 } affected || affected.Any(c => !keys.Contains(c)))
        {
            return Error.Validation("invalid_components", $"Name at least one component from {string.Join(", ", keys)}.");
        }

        if (message.Length is 0 or > 2000)
        {
            return Error.Validation("invalid_message", "Message must be 1 to 2000 characters.");
        }

        var now = time.GetUtcNow();
        var incident = new StatusIncident(Guid.NewGuid(), title, request.Impact, "investigating", [.. affected.Distinct()], now, now, null, by,
            [new StatusIncidentUpdate("investigating", message, now)]);
        await store.CreateAsync(incident, cancellationToken);
        return Result.Success(incident);
    }

    public async Task<Result<StatusIncident>> AddUpdateAsync(Guid id, AddStatusUpdate request, string by, CancellationToken cancellationToken)
    {
        var message = request.Message?.Trim() ?? string.Empty;
        if (request.Status is null || !StatusPage.Statuses.Contains(request.Status))
        {
            return Error.Validation("invalid_status", $"Status must be one of {string.Join(", ", StatusPage.Statuses)}.");
        }

        if (message.Length is 0 or > 2000)
        {
            return Error.Validation("invalid_message", "Message must be 1 to 2000 characters.");
        }

        if (await store.GetAsync(id, cancellationToken) is not { } incident)
        {
            return Error.NotFound("status_incident_not_found", "No such incident.");
        }

        if (incident.ResolvedAt is not null || !await store.AddUpdateAsync(id, new StatusIncidentUpdate(request.Status, message, time.GetUtcNow()), by, cancellationToken))
        {
            return Error.Conflict("status_incident_resolved", "This incident is resolved; open a new one instead.");
        }

        return Result.Success((await store.GetAsync(id, cancellationToken))!);
    }
}
