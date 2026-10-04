using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Serialization;
using SwiftBets.Steward.Application.Ports;
using SwiftBets.Steward.Application.Status;
using SwiftBets.Steward.Domain.Status;

namespace SwiftBets.Steward.Api.Endpoints;

/// <summary>The public status page and the staff endpoints that publish its incidents (D182).</summary>
public static class StatusEndpoints
{
    public const string Permission = "status.write";

    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Public: component states and recent incidents, never who wrote them.
        endpoints.MapGet("/status", async (IComponentStatus components, IStatusStore store, TimeProvider time, CancellationToken cancellationToken) =>
        {
            var incidents = await store.ListAsync(time.GetUtcNow().AddDays(-7), includeAllUnresolved: true, cancellationToken);
            return Results.Json(new
            {
                updatedAt = components.UpdatedAt,
                overall = StatusPage.Overall(components.Components.Select(c => c.State)),
                components = components.Components.Select(c => new { key = c.Key, name = c.Name, state = c.State }),
                incidents = incidents.Select(i => Public(i)),
            }, ContractJson.Options);
        }).AllowAnonymous();

        var staff = endpoints.MapGroup("/status/incidents").RequireAuthorization(Permission);

        staff.MapGet("/", async (IStatusStore store, TimeProvider time, CancellationToken cancellationToken) =>
            Results.Json(new { items = (await store.ListAsync(time.GetUtcNow().AddDays(-90), includeAllUnresolved: true, cancellationToken)).Select(Staff) }, ContractJson.Options));

        staff.MapPost("/", async (CreateStatusIncident request, StatusIncidentHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await handler.CreateAsync(request, By(context), cancellationToken);
            return result.IsSuccess
                ? Results.Json(Staff(result.Value), ContractJson.Options, statusCode: StatusCodes.Status201Created)
                : result.Error!.ToHttpResult(context);
        });

        staff.MapPost("/{id:guid}/updates", async (Guid id, AddStatusUpdate request, StatusIncidentHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await handler.AddUpdateAsync(id, request, By(context), cancellationToken);
            return result.IsSuccess ? Results.Json(Staff(result.Value), ContractJson.Options) : result.Error!.ToHttpResult(context);
        });

        return endpoints;
    }

    private static object Public(StatusIncident i) => new
    {
        id = i.Id, title = i.Title, impact = i.Impact, status = i.Status, components = i.Components,
        createdAt = i.CreatedAt, updatedAt = i.UpdatedAt, resolvedAt = i.ResolvedAt,
        updates = i.Updates.Select(u => new { status = u.Status, message = u.Message, at = u.At }),
    };

    private static object Staff(StatusIncident i) => new
    {
        id = i.Id, title = i.Title, impact = i.Impact, status = i.Status, components = i.Components,
        createdAt = i.CreatedAt, updatedAt = i.UpdatedAt, resolvedAt = i.ResolvedAt, createdBy = i.CreatedBy,
        updates = i.Updates.Select(u => new { status = u.Status, message = u.Message, at = u.At }),
    };

    private static string By(HttpContext context) => context.User.FindFirst("sub")?.Value ?? "unknown";
}
