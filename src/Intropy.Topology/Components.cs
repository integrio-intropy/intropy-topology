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
    private readonly HashSet<string> _publishedMessages = new(StringComparer.Ordinal);
    private readonly List<(PortRef Port, PortDirection Direction)> _ports = [];
    private readonly List<ServiceRef> _services = [];
    private readonly HashSet<string> _serviceAppIds = new(StringComparer.Ordinal);

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

    internal SubscriptionDeclaration AddSubscription(string componentName)
    {
        var subscription = new SubscriptionDeclaration(componentName);
        _subscriptions.Add(subscription);
        return subscription;
    }

    internal void AddPublish(MessageRef message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!_publishedMessages.Add(message.Name))
        {
            throw new InvalidOperationException(
                $"Component '{Name}' already declares publishing the message '{message.Name}'; a component publishes a message at most once.");
        }

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
        if (!_serviceAppIds.Add(service.AppId))
        {
            throw new InvalidOperationException(
                $"Component '{Name}' already declares a call to service '{service.AppId}'.");
        }

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
/// what happens to the channel's other messages. The invariants that hold within one
/// subscription — one handled message per name, all messages on one channel — are checked
/// here, at the declaration call that would break them. Mutable during declaration.</summary>
internal sealed class SubscriptionDeclaration
{
    private readonly string _componentName;
    private readonly List<MessageRef> _messages = [];
    private readonly HashSet<string> _handledMessages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _conditions = new(StringComparer.Ordinal);

    public SubscriptionDeclaration(string componentName) => _componentName = componentName;

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
        if (_messages.Count > 0
            && (_messages[0].PubSubName != message.PubSubName || _messages[0].TopicName != message.TopicName))
        {
            var declared = $"'{_messages[0].TopicName}' on pubsub '{_messages[0].PubSubName}'";
            var incoming = $"'{message.TopicName}' on pubsub '{message.PubSubName}'";
            throw new InvalidOperationException(
                $"Loader '{_componentName}' subscribes to messages on different channels ({declared}, {incoming}); "
                + "a subscription is to one channel, so all of its messages must travel on it.");
        }

        if (!_handledMessages.Add(message.Name))
        {
            throw new InvalidOperationException(
                $"Loader '{_componentName}' already handles the message '{message.Name}'; each message is handled once.");
        }

        _messages.Add(message);
        if (condition is not null)
        {
            _conditions.TryAdd(message.Name, condition);
        }
    }
}
