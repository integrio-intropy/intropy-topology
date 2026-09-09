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

/// <summary>
/// A message group materialized from usage. One group per system, named after
/// <see cref="SystemTopology.SystemName"/>: the group level is the ownership boundary for
/// message identities and their contracts. The nesting is carried in the model now so the
/// versioned interchange stays stable when the boundary changes.
/// </summary>
public sealed record MessageGroupResource
{
    /// <summary>The group's name — the owning system's name.</summary>
    public required string Name { get; init; }

    /// <summary>The messages in the group (sorted by name).</summary>
    public required IReadOnlyList<MessageResource> Messages { get; init; }
}

/// <summary>
/// A message materialized from usage: its logical identity, the transport channel it
/// moves over, and the component names publishing and consuming it, precomputed in
/// ordinal sort order.
/// </summary>
public sealed record MessageResource
{
    /// <summary>The message's logical name.</summary>
    public required string Name { get; init; }

    /// <summary>Full name of the payload contract type transported by the message.</summary>
    public required string ContractTypeName { get; init; }

    /// <summary>The transport channel the message flows over.</summary>
    public required MessageChannel Channel { get; init; }

    /// <summary>Names of components publishing the message (sorted).</summary>
    public required IReadOnlyList<string> Publishers { get; init; }

    /// <summary>Names of components subscribed to the message (sorted).</summary>
    public required IReadOnlyList<string> Subscribers { get; init; }
}

/// <summary>The transport channel a message moves over: one pubsub component and topic.</summary>
public sealed record MessageChannel
{
    /// <summary>The Dapr pubsub component name carrying the message.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic name the message flows over within the pubsub.</summary>
    public required string TopicName { get; init; }
}

/// <summary>An external platform service materialized from component usage.</summary>
public sealed record ServiceResource
{
    /// <summary>The Dapr app ID callers invoke.</summary>
    public required string AppId { get; init; }

    /// <summary>Names of components invoking this service, sorted ordinally.</summary>
    public required IReadOnlyList<string> Consumers { get; init; }
}

/// <summary>A system-owned port materialized from usage.</summary>
public sealed record PortResource
{
    /// <summary>Derives a port's Dapr binding component name: the port name itself.
    /// The single derivation site — every consumer of the name goes through here.</summary>
    public static string DaprComponentNameFor(string portName) => portName;

    /// <summary>The port's name (DNS-1123 label) — its whole identity.</summary>
    public required string Name { get; init; }

    /// <summary>The Dapr binding component name — identical to <see cref="Name"/>.</summary>
    public string DaprComponentName => DaprComponentNameFor(Name);

    /// <summary>The union of directions the port is used in.</summary>
    public required IReadOnlyList<PortDirection> Directions { get; init; }

    /// <summary>Names of components using the port (sorted).</summary>
    public required IReadOnlyList<string> UsedBy { get; init; }
}

