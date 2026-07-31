namespace Intropy.Topology.Model;

/// <summary>
/// One component in the system: an integration block or a container service, together
/// with all of its edges and one activation attribute — an optional cron schedule
/// (decision 0010). Everything else about the workload shape (hosting model, ports,
/// process lifetime) lives in the component's own scaffold, not in the topology.
/// </summary>
public sealed record ComponentModel
{
    /// <summary>The component's name (K8s resource name / Dapr app-id).</summary>
    public required string Name { get; init; }

    /// <summary>The component kind this component was declared as.</summary>
    public required ComponentKind Kind { get; init; }

    /// <summary>The cron schedule that activates the component; null when it runs once
    /// at host start.</summary>
    public required string? Schedule { get; init; }

    /// <summary>Topics the component subscribes to.</summary>
    public required IReadOnlyList<TopicSubscription> Subscribes { get; init; }

    /// <summary>Topics the component publishes to, by output port.</summary>
    public required IReadOnlyList<PublishEdge> Publishes { get; init; }

    /// <summary>Connectors the component uses (explicit From/To usage).</summary>
    public required IReadOnlyList<ConnectorEdge> Connectors { get; init; }

    /// <summary>External platform-service app IDs the component invokes.</summary>
    public required IReadOnlyList<string> Uses { get; init; }
}
