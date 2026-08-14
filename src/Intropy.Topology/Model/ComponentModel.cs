namespace Intropy.Topology.Model;

/// <summary>
/// One component in the system: an integration block or a container service, together
/// with all of its edges. Everything about the workload shape (hosting model, activation,
/// ports, process lifetime) lives in the component's own scaffold, not in the topology.
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

    /// <summary>Ports the component uses (explicit From/To usage).</summary>
    public required IReadOnlyList<PortEdge> Ports { get; init; }

    /// <summary>External platform-service app IDs the component invokes.</summary>
    public required IReadOnlyList<string> Uses { get; init; }

    /// <summary>
    /// The internal receive-to-send queue of a transactional integration, minted by the
    /// materializer; <see langword="null"/> for other kinds. This is workload shape, not an
    /// inter-component edge: nothing declares it, and it never appears in
    /// <see cref="SystemTopology.Topics"/>. It is minted here (not derived per backend) so
    /// every consumer — local generation, Aspire, deployment tooling — agrees on the name.
    /// </summary>
    public required InternalQueue? InternalQueue { get; init; }
}
