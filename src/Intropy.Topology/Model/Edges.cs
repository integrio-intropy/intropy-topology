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
