using Intropy.Topology.Model;

namespace Intropy.Topology.Building;

/// <summary>
/// Base class for builders that declare a topology component and its permitted edges.
/// Each derived builder exposes only the operations valid for its component kind, so
/// invalid edges cannot be declared. Fluent methods return the derived builder type,
/// and <see cref="Component"/> provides the typed component declared by the builder.
/// Not thread-safe.
/// </summary>
/// <typeparam name="TSelf">The concrete component builder type.</typeparam>
/// <typeparam name="TComponent">The typed component the builder declares.</typeparam>
public abstract class ComponentBuilder<TSelf, TComponent>
    where TSelf : ComponentBuilder<TSelf, TComponent>
    where TComponent : Component
{
    private protected ComponentBuilder(TComponent component) => Component = component;

    /// <summary>The typed component this builder declares — hold it to reference the block later.</summary>
    public TComponent Component { get; }

    /// <summary>The fluent self-reference every chainable member returns.</summary>
    private protected abstract TSelf Self { get; }

    /// <summary>Declares that the component invokes an external platform service through Dapr
    /// service invocation — a synchronous request/response call via the sidecar. The topology
    /// records the dependency as a consumer of the service's app ID; it does not define or
    /// deploy the provider.</summary>
    /// <param name="service">The Dapr app identity the component calls.</param>
    public TSelf Calls(ServiceRef service)
    {
        Component.AddService(service);
        return Self;
    }
}

/// <summary>Fluent builder for an extractor: an edge block that pulls or receives data
/// from an external system and publishes it to at least one message.</summary>
public sealed class ExtractorBuilder : ComponentBuilder<ExtractorBuilder, ExtractorComponent>
{
    internal ExtractorBuilder(ExtractorComponent component) : base(component) { }

    private protected override ExtractorBuilder Self => this;

    /// <summary>Declares that the extractor reads from the outside world through a port.</summary>
    /// <param name="port">The port to read from.</param>
    public ExtractorBuilder From(PortRef port)
    {
        Component.AddPort(port, PortDirection.In);
        return this;
    }

    /// <summary>Declares that the extractor publishes a message.</summary>
    /// <param name="message">The message the extractor publishes.</param>
    public ExtractorBuilder Publishes(MessageRef message)
    {
        Component.AddPublish(message);
        return this;
    }
}

/// <summary>Fluent builder for a loader: an edge block that subscribes to exactly one channel —
/// handling one or more of the messages on it — and writes to an external system through a port;
/// loaders publish nothing.</summary>
public sealed class LoaderBuilder : ComponentBuilder<LoaderBuilder, LoaderComponent>
{
    internal LoaderBuilder(LoaderComponent component) : base(component) { }

    private protected override LoaderBuilder Self => this;

    /// <summary>
    /// Declares the loader's subscription, which handles at least the given message and may handle
    /// more of the same channel's messages. All handled messages must travel on one channel — the
    /// subscription's channel is the messages' own — and the subscription renders as one
    /// declarative Dapr <c>Subscription</c> with one routing rule per handled message.
    /// </summary>
    /// <param name="message">The first message whose events the loader consumes; the channel the
    /// message travels on is the subscription's channel.</param>
    /// <param name="when">A content filter for <paramref name="message"/>: a Dapr CEL expression
    /// over the event the message's events must also match, such as
    /// <c>event.data.reason == 'customer-request'</c>. Payload properties are camelCase. Null
    /// handles all of the message's events.</param>
    /// <param name="configure">Declares the subscription's remaining behavior: the channel's other
    /// messages it handles (<see cref="SubscriptionBuilder.AlsoHandles"/>), what happens to its
    /// unhandled messages (<see cref="SubscriptionBuilder.IgnoreOthers"/>), and whether its
    /// messages are delivered in batches (<see cref="SubscriptionBuilder.InBatches"/>).</param>
    /// <exception cref="ArgumentException"><paramref name="when"/> is empty or whitespace.</exception>
    public LoaderBuilder Subscribes(
        MessageRef message,
        string? when = null,
        Action<SubscriptionBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var subscription = new SubscriptionBuilder(Component, Component.AddSubscription());
        subscription.AlsoHandles(message, when);
        configure?.Invoke(subscription);
        return this;
    }

    /// <summary>Declares that the loader writes to the outside world through a port.</summary>
    /// <param name="port">The port to write to.</param>
    public LoaderBuilder To(PortRef port)
    {
        Component.AddPort(port, PortDirection.Out);
        return this;
    }
}

/// <summary>
/// Fluent builder for one subscription: the messages a component handles from a channel, and what
/// happens to the channel's other messages. A message's name is its CloudEvent type; each handled
/// message becomes a routing rule on the rendered Dapr <c>Subscription</c>. Not thread-safe.
/// </summary>
public sealed class SubscriptionBuilder
{
    private readonly LoaderComponent _component;
    private readonly SubscriptionDeclaration _subscription;

    internal SubscriptionBuilder(LoaderComponent component, SubscriptionDeclaration subscription)
    {
        _component = component;
        _subscription = subscription;
    }

    /// <summary>Declares that the subscription also handles the given message, on top of the one
    /// <c>Subscribes</c> requires. All handled messages must travel on the same channel.</summary>
    /// <param name="message">The message to additionally handle.</param>
    /// <param name="when">A content filter: a Dapr CEL expression over the event the message must
    /// also match, such as <c>event.data.reason == 'customer-request'</c>. Payload properties are
    /// camelCase. The sidecar treats the message's events it leaves out as unhandled. Null handles
    /// all of the message's events.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="when"/> is empty or whitespace.</exception>
    public SubscriptionBuilder AlsoHandles(MessageRef message, string? when = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        _subscription.Add(message, ContentFilters.Trimmed(when));
        return this;
    }

    /// <summary>Acknowledges and drops the channel's messages this subscription does not handle,
    /// instead of leaving them for the broker to dead-letter: for a channel that carries messages
    /// meant for other components.</summary>
    public SubscriptionBuilder IgnoreOthers()
    {
        _subscription.Unhandled = UnhandledMessages.Ignore;
        return this;
    }

    /// <summary>Delivers the subscription's messages in batches (Dapr bulk subscribe), for a
    /// component whose pipeline runs a batch at once.</summary>
    /// <param name="maxMessages">The most messages the sidecar collects into one delivery.</param>
    /// <param name="maxWait">How long the sidecar waits to fill a delivery before sending what it has.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxMessages"/> is below 1, or
    /// <paramref name="maxWait"/> is not positive or not a whole number of milliseconds.</exception>
    /// <exception cref="InvalidOperationException">The component already declares its batching.</exception>
    public SubscriptionBuilder InBatches(int maxMessages, TimeSpan maxWait)
    {
        _component.SetBulk(BulkSubscriptions.Create(maxMessages, maxWait));
        return this;
    }
}

/// <summary>Validates and trims a subscription's optional content filter. One definition serves
/// the first handled message (<c>Subscribes</c>) and the added ones (<see cref="SubscriptionBuilder.AlsoHandles"/>)
/// so both record the filter the same way.</summary>
file static class ContentFilters
{
    public static string? Trimmed(string? when)
    {
        if (when is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(when);
        }

        return when?.Trim();
    }
}

/// <summary>Validates and creates a <see cref="BulkSubscription"/>.</summary>
file static class BulkSubscriptions
{
    public static BulkSubscription Create(int maxMessages, TimeSpan maxWait)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMessages, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxWait, TimeSpan.Zero);
        if (maxWait.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ArgumentOutOfRangeException(nameof(maxWait), maxWait,
                "The sidecar batches in whole milliseconds.");

        return new BulkSubscription { MaxMessages = maxMessages, MaxWait = maxWait };
    }
}

/// <summary>Fluent builder for a transactional integration: a synchronous block that
/// reads/writes external systems through ports; it publishes nothing. Its internal
/// receive-to-send queue is minted at materialization (<see cref="ComponentModel.InternalQueue"/>),
/// never declared here.</summary>
public sealed class TransactionalIntegrationBuilder
    : ComponentBuilder<TransactionalIntegrationBuilder, TransactionalIntegrationComponent>
{
    internal TransactionalIntegrationBuilder(TransactionalIntegrationComponent component)
        : base(component) { }

    private protected override TransactionalIntegrationBuilder Self => this;

    /// <summary>Declares that the integration reads from an external system through a port.</summary>
    /// <param name="port">The port to read from.</param>
    public TransactionalIntegrationBuilder From(PortRef port)
    {
        Component.AddPort(port, PortDirection.In);
        return this;
    }

    /// <summary>Declares that the integration writes to an external system through a port.</summary>
    /// <param name="port">The port to write to.</param>
    public TransactionalIntegrationBuilder To(PortRef port)
    {
        Component.AddPort(port, PortDirection.Out);
        return this;
    }
}
