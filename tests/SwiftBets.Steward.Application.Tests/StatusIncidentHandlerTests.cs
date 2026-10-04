using SwiftBets.Steward.Application.Ports;
using SwiftBets.Steward.Application.Status;
using SwiftBets.Steward.Domain.Status;

namespace SwiftBets.Steward.Application.Tests;

public sealed class StatusIncidentHandlerTests
{
    [Fact]
    public async Task Incident_is_opened_updated_and_resolved_then_refuses_more_updates()
    {
        var handler = Handler();
        var created = await handler.CreateAsync(new CreateStatusIncident("Slow deposits", "minor", ["payments"], "Deposits are slow."), "ops1", TestContext.Current.CancellationToken);
        var id = created.Value!.Id;

        (await handler.AddUpdateAsync(id, new AddStatusUpdate("resolved", "Fixed."), "ops1", TestContext.Current.CancellationToken)).Value!.Status.ShouldBe("resolved");
        var late = await handler.AddUpdateAsync(id, new AddStatusUpdate("monitoring", "Again."), "ops1", TestContext.Current.CancellationToken);

        late.IsSuccess.ShouldBeFalse();
        late.Error!.Code.ShouldBe("status_incident_resolved");
    }

    [Fact]
    public async Task Incident_naming_an_unknown_component_is_refused()
    {
        var result = await Handler().CreateAsync(new CreateStatusIncident("Down", "major", ["bingo"], "Down."), "ops1", TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("invalid_components");
    }

    private static StatusIncidentHandler Handler() => new(new MemoryStatusStore(), new Board(), TimeProvider.System);

    private sealed class Board : IComponentStatus
    {
        public IReadOnlyList<ComponentHealth> Components { get; } = [new("payments", "Payments", StatusPage.Operational)];

        public DateTimeOffset? UpdatedAt => null;
    }

    private sealed class MemoryStatusStore : IStatusStore
    {
        private readonly Dictionary<Guid, StatusIncident> _incidents = [];

        public Task CreateAsync(StatusIncident incident, CancellationToken cancellationToken)
        {
            _incidents[incident.Id] = incident;
            return Task.CompletedTask;
        }

        public Task<StatusIncident?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_incidents.GetValueOrDefault(id));

        public Task<bool> AddUpdateAsync(Guid id, StatusIncidentUpdate update, string by, CancellationToken cancellationToken)
        {
            var incident = _incidents[id];
            if (incident.ResolvedAt is not null)
            {
                return Task.FromResult(false);
            }

            _incidents[id] = incident with { Status = update.Status, ResolvedAt = update.Status == StatusPage.Resolved ? update.At : null, Updates = [update, .. incident.Updates] };
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<StatusIncident>> ListAsync(DateTimeOffset since, bool includeAllUnresolved, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StatusIncident>>([.. _incidents.Values]);
    }
}
