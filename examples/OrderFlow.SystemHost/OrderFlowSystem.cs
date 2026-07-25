using Intropy.Topology;

/// <summary>The order-flow system: what exists, and what connects it.</summary>
public sealed class OrderFlowSystem : ISystemDefinition
{
    public string SystemName => "order-flow";

    public void Define(SystemBuilder builder)
    {
        builder.AddExtractor("order-extractor").From(Connectors.OrderExtractorSource).Publishes(Topics.Orders);
        builder.AddLoader("order-loader").Subscribes(Topics.Orders).To(Connectors.OrderLoaderDestination);
    }
}
