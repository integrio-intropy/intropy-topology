using System.Text.Json.Serialization;

namespace Intropy.Topology.Model;

/// <summary>
/// A topic materialized from usage, with its publishing and subscribing component names
/// precomputed in ordinal sort order.
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

/// <summary>An external platform service materialized from component usage.</summary>
public sealed record ServiceResource
{
    /// <summary>The Dapr app ID callers invoke.</summary>
    public required string AppId { get; init; }

    /// <summary>Names of components invoking this service, sorted ordinally.</summary>
    public required IReadOnlyList<string> Consumers { get; init; }
}

/// <summary>A system-owned connector materialized from usage.</summary>
public sealed record ConnectorResource
{
    /// <summary>The connector's name (DNS-1123 label).</summary>
    public required string Name { get; init; }

    /// <summary>The local transport — the kind of Dapr binding the connector materializes during F5 runs.</summary>
    public required Transport Transport { get; init; }

    /// <summary>
    /// The value-free deployed transport shape, or <see langword="null"/> when the local
    /// transport also applies to deployed environments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Transport? DeployedTransport { get; init; }

    /// <summary>The derived Dapr binding component name: <c>binding.&lt;connector-name&gt;</c>.</summary>
    public string DaprComponentName => $"binding.{Name}";

    /// <summary>The union of directions the connector is used in.</summary>
    public required IReadOnlyList<ConnectorDirection> Directions { get; init; }

    /// <summary>Names of components using the connector (sorted).</summary>
    public required IReadOnlyList<string> UsedBy { get; init; }
}

