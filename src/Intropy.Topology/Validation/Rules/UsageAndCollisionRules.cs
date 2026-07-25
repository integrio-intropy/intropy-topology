namespace Intropy.Topology.Validation.Rules;

/// <summary>ITP302 (warning): a component with no edges at all is likely unfinished.</summary>
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
                    "ITP302",
                    DiagnosticSeverity.Warning,
                    "The component has no edges (no subscriptions, publishes, or connectors); it is likely unfinished.",
                    component.Name);
            }
        }
    }
}

/// <summary>
/// ITP401: a pubsub name must not equal a connector's derived Dapr binding component name
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
                "ITP401",
                DiagnosticSeverity.Error,
                $"The pubsub name '{connector.DaprComponentName}' equals the Dapr binding component name derived from connector '{connector.Name}'; both become Dapr Component names in the same namespace.",
                connector.DaprComponentName);
        }
    }
}

/// <summary>
/// ITP402: a service name must not collide with another runtime resource name — a
/// component's, or the run backend's reserved <c>rabbitmq</c> broker — because services
/// and components share one resource-name namespace when the system runs.
/// </summary>
internal sealed class ServiceNameCollisionRule : ITopologyRule
{
    /// <summary>The run backend's pub/sub broker resource; its name is taken.</summary>
    private const string ReservedBrokerName = "rabbitmq";

    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        var componentNames = context.Topology.Components
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var service in context.Topology.Services)
        {
            if (componentNames.Contains(service.Name))
            {
                yield return new TopologyDiagnostic(
                    "ITP402",
                    DiagnosticSeverity.Error,
                    $"The service '{service.Name}' has the same name as a component; services and components share one resource-name namespace.",
                    service.Name);
            }

            if (string.Equals(service.Name, ReservedBrokerName, StringComparison.Ordinal))
            {
                yield return new TopologyDiagnostic(
                    "ITP402",
                    DiagnosticSeverity.Error,
                    $"The service name '{ReservedBrokerName}' is reserved for the run backend's pub/sub broker.",
                    service.Name);
            }
        }
    }
}
