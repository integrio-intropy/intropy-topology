namespace Intropy.Topology.Validation.Rules;

/// <summary>Warning: a component with no edges at all is likely unfinished.</summary>
internal sealed class NoEdgesRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var component in context.Topology.Components)
        {
            var hasEdges =
                component.Subscribes.Count > 0
                || component.Publishes.Count > 0
                || component.Connectors.Count > 0;

            if (!hasEdges)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Warning,
                    "The component has no edges (no subscriptions, publishes, or connectors); it is likely unfinished.",
                    component.Name);
            }
        }
    }
}

/// <summary>
/// A pubsub name must not equal a connector's derived Dapr binding component name
/// (<c>binding.&lt;connector-name&gt;</c>) — both become Dapr Component names in the same
/// Kubernetes namespace.
/// </summary>
internal sealed class PubSubConnectorNameCollisionRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        var pubSubNames = context.Topology.Topics
            .Select(t => t.PubSubName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var connector in context.Topology.Connectors
            .Where(c => pubSubNames.Contains(c.DaprComponentName)))
        {
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"The pubsub name '{connector.DaprComponentName}' equals the Dapr binding component name derived from connector '{connector.Name}'; both become Dapr Component names in the same namespace.",
                connector.DaprComponentName);
        }
    }
}
