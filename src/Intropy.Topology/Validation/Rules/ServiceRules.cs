using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// A component must not use the same service more than once. The materializer
/// deduplicates service usage, so the repetition is visible only in the raw
/// declarations.
/// </summary>
internal sealed class DuplicateServiceUsageRule : IDeclarationRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemBuilder builder)
    {
        foreach (var component in builder.Components)
        {
            foreach (var duplicate in component.ServiceCalls
                .GroupBy(service => service.AppId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Component '{component.Name}' uses service '{duplicate.Key}' more than once.",
                    component.Name);
            }
        }
    }
}

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
