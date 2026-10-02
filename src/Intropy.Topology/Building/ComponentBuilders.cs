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

    /// <summary>Declares that the component invokes an external platform service.</summary>
    /// <param name="service">The Dapr app identity invoked by the component.</param>
    public TSelf Uses(ServiceRef service)
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

/// <summary>Fluent builder for a loader: an edge block that subscribes to exactly one message
/// and writes to an external system through a port; loaders publish nothing.</summary>
public sealed class LoaderBuilder : ComponentBuilder<LoaderBuilder, LoaderComponent>
{
    internal LoaderBuilder(LoaderComponent component) : base(component) { }

    private protected override LoaderBuilder Self => this;

    /// <summary>Declares the single message the loader subscribes to.</summary>
    /// <param name="message">The message whose events the loader consumes.</param>
    public LoaderBuilder Subscribes(MessageRef message)
    {
        Component.AddSubscribe(message);
        return this;
    }

    /// <summary>Declares that the loader writes to the outside world through a port.</summary>
    /// <param name="port">The port to write to.</param>
    public LoaderBuilder To(PortRef port)
    {
        Component.AddPort(port, PortDirection.Out);
        return this;
    }

    /// <summary>
    /// Declares that the loader receives its topic in batches (Dapr bulk subscribe) — for a
    /// loader whose pipeline runs a batch at once. The loader then serves a gRPC app callback
    /// the sidecar delivers to, instead of opening a streaming subscription; hosts give it a
    /// gRPC app channel.
    /// </summary>
    /// <param name="maxMessages">The most messages the sidecar collects into one delivery.</param>
    /// <param name="maxWait">How long the sidecar waits to fill a delivery before sending what it has.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxMessages"/> is below 1, or
    /// <paramref name="maxWait"/> is not positive or not a whole number of milliseconds.</exception>
    /// <exception cref="InvalidOperationException">The loader already declares its batching.</exception>
    public LoaderBuilder InBatches(int maxMessages, TimeSpan maxWait)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMessages, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxWait, TimeSpan.Zero);
        if (maxWait.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ArgumentOutOfRangeException(nameof(maxWait), maxWait,
                "The sidecar batches in whole milliseconds.");

        Component.SetBulk(new BulkSubscription { MaxMessages = maxMessages, MaxWait = maxWait });
        return this;
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
