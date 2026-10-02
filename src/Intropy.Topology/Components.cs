using Intropy.Topology.Model;

namespace Intropy.Topology;

/// <summary>
/// Represents a declared topology component and its recorded edges. Declaration methods
/// record intent; <see cref="SystemBuilder.Build"/> performs completeness and
/// cross-component validation. Instances are mutable during declaration and not thread-safe.
/// </summary>
public abstract class Component
{
    private readonly List<SubscriptionDeclaration> _subscriptions = [];
    private readonly List<MessageRef> _publishes = [];
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

    /// <summary>The declared subscriptions, each with the messages it handles.</summary>
    internal IReadOnlyList<SubscriptionDeclaration> SubscriptionCalls => _subscriptions;

    /// <summary>Every message the component handles, across its subscriptions.</summary>
    internal IEnumerable<MessageRef> SubscribeCalls => _subscriptions.SelectMany(s => s.Messages);

    internal IReadOnlyList<MessageRef> PublishCalls => _publishes;

    internal IReadOnlyList<(PortRef Port, PortDirection Direction)> PortCalls => _ports;

    internal IReadOnlyList<ServiceRef> ServiceCalls => _services;

    internal SubscriptionDeclaration AddSubscription()
    {
        var subscription = new SubscriptionDeclaration();
        _subscriptions.Add(subscription);
        return subscription;
    }

    internal void AddPublish(MessageRef message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _publishes.Add(message);
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

/// <summary>The declared loader: an edge block that subscribes to events and writes
/// to an external system. Obtained from the loader builder's <c>Component</c> property.</summary>
public sealed class LoaderComponent : Component
{
    internal LoaderComponent(string name) : base(name, ComponentKind.Loader) { }

    /// <summary>The batching declared with <c>InBatches</c>; null when the loader consumes one
    /// message at a time.</summary>
    internal BulkSubscription? Bulk { get; private set; }

    internal void SetBulk(BulkSubscription bulk)
    {
        if (Bulk is not null)
            throw new InvalidOperationException($"Loader '{Name}' already declares how it batches its messages.");
        Bulk = bulk;
    }
}

/// <summary>The declared transactional integration: a synchronous block that reads/writes
/// external systems through ports. Obtained from the integration builder's
/// <c>Component</c> property.</summary>
public sealed class TransactionalIntegrationComponent : Component
{
    internal TransactionalIntegrationComponent(string name)
        : base(name, ComponentKind.TransactionalIntegration) { }
}

/// <summary>One declared subscription: the messages a component handles from one channel, and
/// what happens to the channel's other messages. Mutable during declaration.</summary>
internal sealed class SubscriptionDeclaration
{
    private readonly List<MessageRef> _messages = [];
    private readonly Dictionary<string, string> _conditions = new(StringComparer.Ordinal);

    /// <summary>The messages the subscription handles, in declaration order.</summary>
    public IReadOnlyList<MessageRef> Messages => _messages;

    /// <summary>The content filter (a CEL expression) of each handled message that has one, by
    /// message name.</summary>
    public IReadOnlyDictionary<string, string> Conditions => _conditions;

    /// <summary>What happens to the channel's messages the subscription does not handle.</summary>
    public UnhandledMessages Unhandled { get; set; } = UnhandledMessages.DeadLetter;

    public void Add(MessageRef message, string? condition)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Add(message);
        if (condition is not null)
        {
            _conditions.TryAdd(message.Name, condition);
        }
    }
}
