using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>Warning: a component with no edges at all is likely unfinished.</summary>
internal sealed class NoEdgesRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var component in topology.Components)
        {
            var hasEdges =
                component.Subscribes.Count > 0
                || component.Publishes.Count > 0
                || component.Ports.Count > 0;

            if (!hasEdges)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Warning,
                    "The component has no edges (no subscriptions, publishes, or ports); it is likely unfinished.",
                    component.Name);
            }
        }
    }
}

/// <summary>
/// A pubsub name must not equal a port's Dapr binding component name (the port name
/// itself) — both become Dapr Component names in the same Kubernetes namespace.
/// </summary>
internal sealed class PubSubPortNameCollisionRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        var pubSubNames = topology.Topics
            .Select(t => t.PubSubName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var port in topology.Ports
            .Where(c => pubSubNames.Contains(c.DaprComponentName)))
        {
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"The pubsub name '{port.DaprComponentName}' equals port '{port.Name}'s Dapr binding component name; both become Dapr Component names in the same namespace.",
                port.DaprComponentName);
        }
    }
}
