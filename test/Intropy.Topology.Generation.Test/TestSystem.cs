using Intropy.Topology;

namespace Intropy.Topology.Generation.Test;

public sealed record RawOrder(string OrderNumber);
public sealed record FulfillmentOrder(string OrderNumber);

// Exactly one ISystemDefinition lives in this assembly so IntropyGenerate/SystemDiscovery resolve it.
// Two extractor -> loader flows across two pub/subs (pubsub-a, pubsub-b); connectors resolve
// locally to ./test folders via the development definition below.
public sealed class OrderSystem : ISystemDefinition
{
    public static readonly TopicRef<RawOrder> Raw = TopicRef<RawOrder>.Define("pubsub-a", "order-raw");
    public static readonly TopicRef<FulfillmentOrder> Fulfillment = TopicRef<FulfillmentOrder>.Define("pubsub-b", "order-fulfillment");
    public static readonly ConnectorRef Webshop = ConnectorRef.Define("webshop", Transport.Sftp());
    public static readonly ConnectorRef Erp = ConnectorRef.Define("erp", Transport.Sftp());

    public string SystemName => "order-fulfillment";

    public void Define(SystemBuilder builder)
    {
        builder.AddExtractor("order-extractor").From(Erp).Publishes(Raw).WithSchedule("*/5 * * * *");
        builder.AddLoader("order-loader").Subscribes(Raw).To(Webshop);
        builder.AddExtractor("fulfillment-extractor").Publishes(Fulfillment);
        builder.AddLoader("fulfillment-loader").Subscribes(Fulfillment);
        builder.AddLoader("raw-audit").Subscribes(Raw);
    }
}

/// <summary>The local file resolutions backing <see cref="OrderSystem"/>'s connectors.</summary>
public sealed class OrderDevelopment : IDevelopmentDefinition
{
    public void Define(DevelopmentBuilder development)
    {
        development.Files(OrderSystem.Webshop).RootPath("./test/webshop");
        development.Files(OrderSystem.Erp).RootPath("./test/erp");
    }
}
