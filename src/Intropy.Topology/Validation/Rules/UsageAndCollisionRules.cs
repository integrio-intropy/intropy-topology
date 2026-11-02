using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>Warning: a component with no edges at all is likely unfinished.</summary>
internal sealed class NoEdgesRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        // A job declares no edges by design: its work is its own.
        foreach (var component in topology.Components.Where(c => c.Kind is not ComponentKind.Job))
        {
            var hasEdges =
                component.Subscribes.Count > 0
                || component.Publishes.Count > 0
                || component.Ports.Count > 0
                || component.Endpoints is { Count: > 0 };

            if (!hasEdges)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Warning,
                    "The component has no edges (no subscriptions, publishes, ports, or endpoints); it is likely unfinished.",
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
            .Concat(topology.Components.Select(c => c.InternalQueue?.PubSubName).OfType<string>())
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

/// <summary>
/// An endpoint's name is its identity across the system: one gateway serves it, and it must not
/// equal a port's name — both name a resource of the system, and tooling keys them in one
/// namespace.
/// </summary>
internal sealed class EndpointNameCollisionRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        var portNames = topology.Ports.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var servers = topology.Components
            .SelectMany(c => (c.Endpoints ?? []).Select(e => (e.EndpointName, Component: c.Name)))
            .GroupBy(e => e.EndpointName, StringComparer.Ordinal);

        foreach (var endpoint in servers)
        {
            if (endpoint.Count() > 1)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"The endpoint '{endpoint.Key}' is served by {string.Join(", ", endpoint.Select(e => $"'{e.Component}'"))}; one gateway serves each endpoint.",
                    endpoint.Key);
            }

            if (portNames.Contains(endpoint.Key))
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"The endpoint '{endpoint.Key}' has the same name as a port; endpoints and ports share one namespace.",
                    endpoint.Key);
            }
        }
    }
}

/// <summary>
/// The state components the topology mints — a processor's plain and outbox stores, a loader's
/// workflow state store — are Dapr components named after their component, so they must not take
/// the name of a port's binding, of a pubsub, or of another minted state component: all of them
/// become Dapr Component names in the same namespace, and generation writes each to a file named
/// after it, so a duplicate silently overwrites the other.
/// </summary>
internal sealed class StateStoreNameCollisionRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        var taken = topology.Ports.Select(p => (Name: p.DaprComponentName, What: $"port '{p.Name}'s binding"))
            .Concat(topology.Topics.Select(t => (Name: t.PubSubName, What: "a pubsub")))
            .Concat(topology.Components.Select(c => c.InternalQueue?.PubSubName).OfType<string>()
                .Select(n => (Name: n, What: "a pubsub")))
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().What, StringComparer.Ordinal);

        var minted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (store, owner) in MintedStores(topology))
        {
            if (taken.TryGetValue(store, out var what) || minted.TryGetValue(store, out what))
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{owner} state component '{store}' has the same name as {what}; both become Dapr Component names in the same namespace.",
                    store);
                continue;
            }

            minted[store] = $"{owner} state component";
        }
    }

    private static IEnumerable<(string Store, string Owner)> MintedStores(SystemTopology topology)
    {
        foreach (var component in topology.Components)
        {
            if (component.State is { } state)
            {
                var owner = $"{component.Kind} '{component.Name}'s";
                yield return (state.StateStoreName, owner);
                yield return (state.OutboxStateStoreName, owner);
            }

            if (component.WorkflowState is { } workflow)
            {
                yield return (workflow.StateStoreName, $"{component.Kind} '{component.Name}'s workflow");
            }
        }
    }
}
