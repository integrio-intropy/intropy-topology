namespace Intropy.Topology.Model;

/// <summary>
/// A topic materialized from usage, with both sides of the edge precomputed
/// (<see cref="Publishers"/> and <see cref="Subscribers"/> feed the future Dapr pubsub
/// component's <c>scopes</c>).
/// </summary>
public sealed record TopicResource
{
    /// <summary>The Dapr pubsub component name.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic name.</summary>
    public required string TopicName { get; init; }

    /// <summary>Full name of the event contract type carried on the topic.</summary>
    public required string ContractTypeName { get; init; }

    /// <summary>Names of components publishing to the topic (sorted).</summary>
    public required IReadOnlyList<string> Publishers { get; init; }

    /// <summary>Names of components subscribed to the topic (sorted).</summary>
    public required IReadOnlyList<string> Subscribers { get; init; }
}

/// <summary>A system-owned connector materialized from usage.</summary>
public sealed record ConnectorResource
{
    /// <summary>The connector's name (DNS-1123 label).</summary>
    public required string Name { get; init; }

    /// <summary>The transport — the kind of Dapr binding the connector materializes as.</summary>
    public required Transport Transport { get; init; }

    /// <summary>The derived Dapr binding component name: <c>binding.&lt;connector-name&gt;</c>.</summary>
    public string DaprComponentName => $"binding.{Name}";

    /// <summary>The union of directions the connector is used in.</summary>
    public required IReadOnlyList<ConnectorDirection> Directions { get; init; }

    /// <summary>Names of components using the connector (sorted).</summary>
    public required IReadOnlyList<string> UsedBy { get; init; }
}

