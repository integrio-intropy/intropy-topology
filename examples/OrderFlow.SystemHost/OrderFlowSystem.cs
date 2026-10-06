using Intropy.Topology;

/// <summary>The order-flow system: what exists, and what connects it.</summary>
public sealed class OrderFlowSystem : ISystemDefinition
{
    /// <inheritdoc />
    public string SystemName => "order-flow";

    /// <inheritdoc />
    public void Define(SystemBuilder builder)
    {
        builder.AddExtractor(Components.OrderExtractor)
            .From(Ports.OrderExtractorSource)
            .Publishes(Messages.Orders)
            .Uses(Services.Idempotency)
            .Uses(Services.BusinessIncidents);
        builder.AddLoader(Components.OrderLoader)
            .Subscribes(Messages.Orders)
            .To(Ports.OrderLoaderDestination)
            .Uses(Services.Idempotency)
            .Uses(Services.BusinessIncidents);
    }
}
