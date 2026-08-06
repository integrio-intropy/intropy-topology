using Intropy.Topology;

/// <summary>The order-flow system: what exists, and what connects it.</summary>
public sealed class OrderFlowSystem : ISystemDefinition
{
    /// <inheritdoc />
    public string SystemName => "order-flow";

    /// <inheritdoc />
    public void Define(SystemBuilder builder)
    {
        builder.AddExtractor("order-extractor")
            .From(Connectors.OrderExtractorSource)
            .Publishes(Topics.Orders)
            .Uses(Services.Idempotency)
            .Uses(Services.BusinessIncidents);
        builder.AddLoader("order-loader")
            .Subscribes(Topics.Orders)
            .To(Connectors.OrderLoaderDestination)
            .Uses(Services.Idempotency)
            .Uses(Services.BusinessIncidents);
    }
}
