using Intropy.Topology;

namespace Intropy.Topology.Generation.Test;

public sealed record RawOrder(string OrderNumber);
public sealed record FulfillmentOrder(string OrderNumber);

// Exactly one ISystemDefinition lives in this assembly so IntropyGenerate/SystemDiscovery resolve it.
// Two extractor -> loader flows across two pub/subs (pubsub-a, pubsub-b); ports resolve
// locally to ./test folders via the development definition below.
public sealed class OrderSystem : ISystemDefinition
{
    public static readonly MessageRef<RawOrder> Raw = MessageRef<RawOrder>.Define("order-raw", "pubsub-a");
    public static readonly MessageRef<FulfillmentOrder> Fulfillment = MessageRef<FulfillmentOrder>.Define("order-fulfillment", "pubsub-b");
    public static readonly PortRef Webshop = PortRef.Define("webshop");
    public static readonly PortRef Erp = PortRef.Define("erp");

    public string SystemName => "order-fulfillment";

    public void Define(SystemBuilder builder)
    {
        builder.AddExtractor("order-extractor").From(Erp).Publishes(Raw);
        builder.AddLoader("order-loader").Subscribes(Raw).To(Webshop);
        builder.AddExtractor("fulfillment-extractor").Publishes(Fulfillment);
        builder.AddLoader("fulfillment-loader").Subscribes(Fulfillment);
        builder.AddLoader("raw-audit").Subscribes(Raw);
        builder.AddTransactionalIntegration("price-sync").From(Erp).To(Webshop);
    }
}

/// <summary>The local file resolutions backing <see cref="OrderSystem"/>'s ports.</summary>
public sealed class OrderDevelopment : IDevelopmentDefinition
{
    public void Define(DevelopmentBuilder development)
    {
        development.Files(OrderSystem.Webshop).RootPath("./test/webshop");
        development.Files(OrderSystem.Erp).RootPath("./test/erp");
    }
}
