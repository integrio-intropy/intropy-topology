namespace Intropy.Topology.Model;

/// <summary>
/// The immutable, validated topology of one integration system: its components (in
/// declaration order) and the resources materialized from their usage (sorted).
/// Plain serializable data — generation tooling consumes this model.
/// </summary>
public sealed record SystemTopology
{
    /// <summary>The system's name (DNS-1123 label).</summary>
    public required string SystemName { get; init; }

    /// <summary>All components, in declaration order.</summary>
    public required IReadOnlyList<ComponentModel> Components { get; init; }

    /// <summary>Topics materialized from publish and subscribe edges.</summary>
    public required IReadOnlyList<TopicResource> Topics { get; init; }

    /// <summary>Connectors materialized from connector usage.</summary>
    public required IReadOnlyList<ConnectorResource> Connectors { get; init; }
}
