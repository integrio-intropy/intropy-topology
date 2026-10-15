using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>A service app ID must not collide with a topology component app ID.</summary>
internal sealed class ServiceAppIdCollisionRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        var componentNames = topology.Components
            .Select(component => component.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var service in topology.Services.Where(service => componentNames.Contains(service.AppId)))
        {
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"Service app ID '{service.AppId}' collides with a topology component app ID.",
                service.AppId);
        }
    }
}
