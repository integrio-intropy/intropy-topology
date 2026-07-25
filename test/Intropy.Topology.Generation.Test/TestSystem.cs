using Intropy.Topology;

namespace Intropy.Topology.Generation.Test;

public sealed record RawOrder(string OrderNumber);
public sealed record FulfillmentOrder(string OrderNumber);

// Exactly one ISystemDefinition lives in this assembly so IntropyGenerate/SystemDiscovery resolve it.
// Two extractor -> loader flows across two pub/subs (pubsub-a, pubsub-b); webshop + erp file connectors.
public sealed class OrderSystem : ISystemDefinition
{
    public static readonly TopicRef<RawOrder> Raw = TopicRef<RawOrder>.Define("pubsub-a", "order-raw");
    public static readonly TopicRef<FulfillmentOrder> Fulfillment = TopicRef<FulfillmentOrder>.Define("pubsub-b", "order-fulfillment");
    public static readonly ConnectorRef Webshop = ConnectorRef.Define("webshop", Transport.File("./test/webshop"));
    public static readonly ConnectorRef Erp = ConnectorRef.Define("erp", Transport.File("./test/erp"));
    public static readonly ServiceRef Idempotency = ServiceRef.Define("idempotency", Service.Container("docker.io/library/redis:7", 6379));

    public string SystemName => "order-fulfillment";

    public void Define(SystemBuilder builder)
    {
        builder.AddExtractor("order-extractor").From(Webshop).Publishes(Raw).WithSchedule("*/5 * * * *");
        builder.AddLoader("order-loader").Subscribes(Raw).To(Erp);
        builder.AddExtractor("fulfillment-extractor").Publishes(Fulfillment);
        builder.AddLoader("fulfillment-loader").Subscribes(Fulfillment);
        builder.UsesService(Idempotency);
    }
}
