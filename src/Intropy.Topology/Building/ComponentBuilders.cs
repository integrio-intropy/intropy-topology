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

    /// <summary>Declares that the extractor is activated on a cron schedule (standard
    /// five-field cron or a macro such as <c>@daily</c>). Without a schedule the
    /// extractor runs once at host start. Syntax is validated at
    /// <see cref="SystemBuilder.Build"/> (ITP210); a later call replaces the value.</summary>
    /// <param name="cron">The cron expression.</param>
    /// <exception cref="ArgumentException">The expression is null or whitespace.</exception>
    public ExtractorBuilder WithSchedule(string cron)
    {
        Component.SetSchedule(cron);
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
