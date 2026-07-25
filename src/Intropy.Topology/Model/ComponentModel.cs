namespace Intropy.Topology.Model;

/// <summary>
/// One component in the system: an integration block or a container service, together
/// with all of its edges. How the component is activated at runtime (schedule vs.
/// long-running) is not modeled here — the topology records only the edges between
/// components; workload shape lives in the component's own scaffold.
/// </summary>
public sealed record ComponentModel
{
    /// <summary>The component's name (K8s resource name / Dapr app-id).</summary>
    public required string Name { get; init; }

    /// <summary>The component kind this component was declared as.</summary>
    public required ComponentKind Kind { get; init; }

    /// <summary>Topics the component subscribes to.</summary>
    public required IReadOnlyList<TopicSubscription> Subscribes { get; init; }

    /// <summary>Topics the component publishes to, by output port.</summary>
    public required IReadOnlyList<PublishEdge> Publishes { get; init; }

    /// <summary>Connectors the component uses (explicit From/To usage).</summary>
    public required IReadOnlyList<ConnectorEdge> Connectors { get; init; }
}
