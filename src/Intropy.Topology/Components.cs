using Intropy.Topology.Model;

namespace Intropy.Topology;

/// <summary>
/// The shared substance of every declared component: its identity plus the recorded
/// declaration calls. Block-specific subclasses (<see cref="ExtractorComponent"/>, ...)
/// give each declaration a typed, holdable handle; the fluent builders in
/// <c>Intropy.Topology.Building</c> are the only writers. Calls only record intent;
/// completeness and cross-component checks run when <see cref="SystemBuilder.Build"/>
/// is called, so every violation in the system is reported at once. Not thread-safe.
/// </summary>
public abstract class Component
{
    /// <summary>The output port name recorded for every <c>Publishes</c> call.</summary>
    internal const string DefaultPort = "default";

    private readonly List<TopicRef> _subscribes = [];
    private readonly List<(string Port, TopicRef Topic)> _publishes = [];
    private readonly List<(ConnectorRef Connector, ConnectorDirection Direction)> _connectors = [];

    private protected Component(string name, ComponentKind kind)
    {
        Name = name;
        Kind = kind;
    }

    /// <summary>The component's name (K8s resource name / Dapr app-id).</summary>
    public string Name { get; }

    /// <summary>The component's block kind.</summary>
    public ComponentKind Kind { get; }

    internal IReadOnlyList<TopicRef> SubscribeCalls => _subscribes;

    internal IReadOnlyList<(string Port, TopicRef Topic)> PublishCalls => _publishes;

    internal IReadOnlyList<(ConnectorRef Connector, ConnectorDirection Direction)> ConnectorCalls => _connectors;

    internal void AddSubscribe(TopicRef topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        _subscribes.Add(topic);
    }

    internal void AddPublish(TopicRef topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        _publishes.Add((DefaultPort, topic));
    }

    internal void AddConnector(ConnectorRef connector, ConnectorDirection direction)
    {
        ArgumentNullException.ThrowIfNull(connector);
        _connectors.Add((connector, direction));
    }
}

/// <summary>The declared extractor: an edge block that pulls or receives data from an
/// external system and publishes it. Obtained from the extractor builder's
/// <c>Component</c> property.</summary>
public sealed class ExtractorComponent : Component
{
    internal ExtractorComponent(string name) : base(name, ComponentKind.Extractor) { }
}

/// <summary>The declared loader: an edge block that consumes exactly one topic and writes
/// to an external system. Obtained from the loader builder's <c>Component</c> property.</summary>
public sealed class LoaderComponent : Component
{
    internal LoaderComponent(string name) : base(name, ComponentKind.Loader) { }
}

/// <summary>The declared transactional integration: a synchronous block that reads/writes
/// external systems through connectors. Obtained from the integration builder's
/// <c>Component</c> property.</summary>
public sealed class TransactionalIntegrationComponent : Component
{
    internal TransactionalIntegrationComponent(string name)
        : base(name, ComponentKind.TransactionalIntegration) { }
}
