using Intropy.Topology.Model;

namespace Intropy.Topology.Building;

/// <summary>
/// Grammar shared by every component builder. The self type keeps the fluent chain on the
/// concrete builder, whose members are exactly that component's legal grammar — declaring
/// an edge a block cannot have is a compile error, not a validation diagnostic. The
/// component type is the typed handle the builder declares, exposed via
/// <see cref="Component"/>. Not thread-safe.
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
}

/// <summary>Fluent builder for an extractor: an edge block that pulls or receives data
/// from an external system and publishes it to at least one topic.</summary>
public sealed class ExtractorBuilder : ComponentBuilder<ExtractorBuilder, ExtractorComponent>
{
    internal ExtractorBuilder(ExtractorComponent component) : base(component) { }

    /// <summary>Declares that the extractor reads from the outside world through a connector.</summary>
    /// <param name="connector">The connector to read from.</param>
    public ExtractorBuilder From(ConnectorRef connector)
    {
        Component.AddConnector(connector, ConnectorDirection.In);
        return this;
    }

    /// <summary>Declares that the extractor publishes to a topic.</summary>
    /// <param name="topic">The topic to publish to.</param>
    public ExtractorBuilder Publishes(TopicRef topic)
    {
        Component.AddPublish(topic);
        return this;
    }
}

/// <summary>Fluent builder for a loader: an edge block that subscribes to exactly one topic
/// and writes to an external system through a connector; loaders publish nothing.</summary>
public sealed class LoaderBuilder : ComponentBuilder<LoaderBuilder, LoaderComponent>
{
    internal LoaderBuilder(LoaderComponent component) : base(component) { }

    /// <summary>Declares the single topic the loader subscribes to.</summary>
    /// <param name="topic">The topic whose events the loader consumes.</param>
    public LoaderBuilder Subscribes(TopicRef topic)
    {
        Component.AddSubscribe(topic);
        return this;
    }

    /// <summary>Declares that the loader writes to the outside world through a connector.</summary>
    /// <param name="connector">The connector to write to.</param>
    public LoaderBuilder To(ConnectorRef connector)
    {
        Component.AddConnector(connector, ConnectorDirection.Out);
        return this;
    }
}

/// <summary>Fluent builder for a transactional integration: a synchronous block that
/// reads/writes external systems through connectors; it publishes no topics (v1).</summary>
public sealed class TransactionalIntegrationBuilder
    : ComponentBuilder<TransactionalIntegrationBuilder, TransactionalIntegrationComponent>
{
    internal TransactionalIntegrationBuilder(TransactionalIntegrationComponent component)
        : base(component) { }

    /// <summary>Declares that the integration reads from an external system through a connector.</summary>
    /// <param name="connector">The connector to read from.</param>
    public TransactionalIntegrationBuilder From(ConnectorRef connector)
    {
        Component.AddConnector(connector, ConnectorDirection.In);
        return this;
    }

    /// <summary>Declares that the integration writes to an external system through a connector.</summary>
    /// <param name="connector">The connector to write to.</param>
    public TransactionalIntegrationBuilder To(ConnectorRef connector)
    {
        Component.AddConnector(connector, ConnectorDirection.Out);
        return this;
    }
}
