namespace Intropy.Topology.Model;

/// <summary>A topic a component subscribes to.</summary>
public sealed record TopicSubscription
{
    /// <summary>The Dapr pubsub component name.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic name.</summary>
    public required string TopicName { get; init; }
}

/// <summary>A component publishing to a topic through a named output port.</summary>
public sealed record PublishEdge
{
    /// <summary>The output port name (<c>default</c> when no port was given).</summary>
    public required string Port { get; init; }

    /// <summary>The Dapr pubsub component name.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic name.</summary>
    public required string TopicName { get; init; }
}

/// <summary>A component using a connector in one direction.</summary>
public sealed record ConnectorEdge
{
    /// <summary>The connector's name.</summary>
    public required string ConnectorName { get; init; }

    /// <summary>The direction the connector is used in.</summary>
    public required ConnectorDirection Direction { get; init; }
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
