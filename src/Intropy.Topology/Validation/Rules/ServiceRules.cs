namespace Intropy.Topology.Validation.Rules;

/// <summary>ITP301/ITP402: services cannot repeat per component or collide with component app IDs.</summary>
internal sealed class ServiceUsageRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var component in context.Builder.Components)
        {
            foreach (var duplicate in component.ServiceCalls
                .GroupBy(service => service.AppId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                yield return new TopologyDiagnostic(
                    "ITP301",
                    DiagnosticSeverity.Error,
                    $"Component '{component.Name}' uses service '{duplicate.Key}' more than once.",
                    component.Name);
            }
        }

        var componentNames = context.Topology.Components
            .Select(component => component.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var service in context.Topology.Services.Where(service => componentNames.Contains(service.AppId)))
        {
            yield return new TopologyDiagnostic(
                "ITP402",
                DiagnosticSeverity.Error,
                $"Service app ID '{service.AppId}' collides with a topology component app ID.",
                service.AppId);
        }
    }
}
