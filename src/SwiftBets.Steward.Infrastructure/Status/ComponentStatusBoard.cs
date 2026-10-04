using Microsoft.Extensions.Options;
using SwiftBets.Steward.Application.Ports;
using SwiftBets.Steward.Domain.Status;

namespace SwiftBets.Steward.Infrastructure.Status;

/// <summary>Holds the latest poll result per component; every configured component starts unknown.</summary>
public sealed class ComponentStatusBoard(IOptions<StatusOptions> options) : IComponentStatus
{
    private volatile IReadOnlyList<ComponentHealth> _components = [.. options.Value.Components.Select(c => new ComponentHealth(c.Key, c.Name, StatusPage.Unknown))];

    public IReadOnlyList<ComponentHealth> Components => _components;

    public DateTimeOffset? UpdatedAt { get; private set; }

    public void Publish(IReadOnlyList<ComponentHealth> components, DateTimeOffset at)
    {
        _components = components;
        UpdatedAt = at;
    }
}
