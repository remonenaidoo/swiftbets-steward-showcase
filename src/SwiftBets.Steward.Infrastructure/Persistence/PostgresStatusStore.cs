using Dapper;
using Npgsql;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Steward.Application.Ports;
using SwiftBets.Steward.Domain.Status;

namespace SwiftBets.Steward.Infrastructure.Persistence;

public sealed class PostgresStatusStore(NpgsqlDataSource dataSource) : IStatusStore
{
    private static readonly SqlResources Sql = SqlResources.For<PostgresStatusStore>();

    public async Task CreateAsync(StatusIncident incident, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Status.Insert"), new
        {
            incident.Id, incident.Title, incident.Impact, incident.Status, Components = incident.Components.ToArray(), incident.CreatedAt, incident.UpdatedAt, incident.CreatedBy,
        }, transaction, cancellationToken: cancellationToken));
        foreach (var update in incident.Updates)
        {
            await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Status.InsertUpdate"), new { incident.Id, update.At, update.Status, update.Message, By = incident.CreatedBy }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<StatusIncident?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<IncidentRow>(new CommandDefinition(Sql.Get("Status.Get"), new { Id = id }, cancellationToken: cancellationToken));
        var incidents = await WithUpdatesAsync(connection, [.. rows], cancellationToken);
        return incidents.Count == 0 ? null : incidents[0];
    }

    public async Task<bool> AddUpdateAsync(Guid id, StatusIncidentUpdate update, string by, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var moved = await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Status.Advance"), new { Id = id, update.Status, update.At }, transaction, cancellationToken: cancellationToken));
        if (moved == 0)
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Status.InsertUpdate"), new { Id = id, update.At, update.Status, update.Message, By = by }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<StatusIncident>> ListAsync(DateTimeOffset since, bool includeAllUnresolved, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<IncidentRow>(new CommandDefinition(Sql.Get("Status.List"), new { Since = since, AllUnresolved = includeAllUnresolved }, cancellationToken: cancellationToken));
        return await WithUpdatesAsync(connection, [.. rows], cancellationToken);
    }

    private static async Task<IReadOnlyList<StatusIncident>> WithUpdatesAsync(NpgsqlConnection connection, IReadOnlyList<IncidentRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var updates = (await connection.QueryAsync<UpdateRow>(new CommandDefinition(Sql.Get("Status.Updates"), new { Ids = rows.Select(r => r.Id).ToArray() }, cancellationToken: cancellationToken)))
            .ToLookup(u => u.IncidentId);
        return [.. rows.Select(r => new StatusIncident(r.Id, r.Title, r.Impact, r.Status, r.Components, Utc(r.CreatedAt), Utc(r.UpdatedAt), r.ResolvedAt is { } resolved ? Utc(resolved) : null, r.CreatedBy,
            [.. updates[r.Id].Select(u => new StatusIncidentUpdate(u.Status, u.Message, Utc(u.At)))]))];
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    // A class with setters, because Dapper cannot match a text[] column to a constructor parameter.
    private sealed class IncidentRow
    {
        public Guid Id { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Impact { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string[] Components { get; init; } = [];

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public DateTime? ResolvedAt { get; init; }

        public string CreatedBy { get; init; } = string.Empty;
    }

    private sealed record UpdateRow(Guid IncidentId, string Status, string Message, DateTime At);
}
