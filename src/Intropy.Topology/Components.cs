using Intropy.Topology.Model;

namespace Intropy.Topology;

/// <summary>
/// Represents a declared topology component and its recorded edges. Declaration methods
/// record intent; <see cref="SystemBuilder.Build"/> performs completeness and
/// cross-component validation. Instances are mutable during declaration and not thread-safe.
/// </summary>
public abstract class Component
{
    private readonly List<TopicRef> _subscribes = [];
    private readonly List<TopicRef> _publishes = [];
    private readonly List<(PortRef Port, PortDirection Direction)> _ports = [];
    private readonly List<ServiceRef> _services = [];

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

    internal IReadOnlyList<TopicRef> PublishCalls => _publishes;

    internal IReadOnlyList<(PortRef Port, PortDirection Direction)> PortCalls => _ports;

    internal IReadOnlyList<ServiceRef> ServiceCalls => _services;

    internal void AddSubscribe(TopicRef topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        _subscribes.Add(topic);
    }

    internal void AddPublish(TopicRef topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        _publishes.Add(topic);
    }

    internal void AddPort(PortRef port, PortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(port);
        _ports.Add((port, direction));
    }

    internal void AddService(ServiceRef service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _services.Add(service);
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
/// external systems through ports. Obtained from the integration builder's
/// <c>Component</c> property.</summary>
public sealed class TransactionalIntegrationComponent : Component
{
    internal TransactionalIntegrationComponent(string name)
        : base(name, ComponentKind.TransactionalIntegration) { }
}
