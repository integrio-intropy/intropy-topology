using System.Text.Json.Serialization;

namespace Intropy.Topology.Model;

/// <summary>A topic a component subscribes to.</summary>
public sealed record TopicSubscription
{
    /// <summary>The Dapr pubsub component name.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic name.</summary>
    public required string TopicName { get; init; }

    /// <summary>Set when the subscription delivers in batches (Dapr bulk subscribe): the
    /// component then receives through a gRPC app callback instead of a streaming
    /// subscription. Null for one message at a time.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BulkSubscription? Bulk { get; init; }
}

/// <summary>How a bulk subscription batches its messages.</summary>
public sealed record BulkSubscription
{
    /// <summary>The most messages the sidecar collects into one delivery.</summary>
    public required int MaxMessages { get; init; }

    /// <summary>How long the sidecar waits to fill a delivery before sending what it has.</summary>
    public required TimeSpan MaxWait { get; init; }
}

/// <summary>A component publishing to a topic.</summary>
public sealed record PublishEdge
{
    /// <summary>The Dapr pubsub component name.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic name.</summary>
    public required string TopicName { get; init; }
}

/// <summary>A component using a port in one direction.</summary>
public sealed record PortEdge
{
    /// <summary>The port's name.</summary>
    public required string PortName { get; init; }

    /// <summary>The direction the port is used in.</summary>
    public required PortDirection Direction { get; init; }
}

/// <summary>
/// A transactional integration's internal receive-to-send hop. Not a user-declared topic:
/// the names are minted from the component name at materialization, so nothing outside the
/// component can subscribe to or publish on it.
/// </summary>
public sealed record InternalQueue
{
    /// <summary>The Dapr pubsub component name backing the hop.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic the receive pipeline publishes and the send pipeline subscribes to.</summary>
    public required string TopicName { get; init; }
}
