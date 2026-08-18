using System.Text.Json;
using Intropy.Topology.Model;

namespace Intropy.Topology.Test.Building;

/// <summary>
/// The living spec: mirrors the SystemHost sample from the README and asserts the full
/// shape of the materialized model.
/// </summary>
public class EndToEndTests
{
    private static class ProductFlow
    {
        public static readonly TopicRef<RawEvent> Raw =
            TopicRef<RawEvent>.Define("product-distribution-pubsub", "product-raw");
    }

    private static class PriceFlow
    {
        public static readonly TopicRef<RawEvent> Raw =
            TopicRef<RawEvent>.Define("product-distribution-pubsub", "price-raw");
    }

    private static readonly PortRef s_pim = PortRef.Define("pim");
    private static readonly PortRef s_erp = PortRef.Define("erp");

    private static SystemBuilder DeclareSystem()
    {
        var s = SystemBuilder.Create("product-distribution");

        s.AddExtractor("pim-extractor")
            .From(s_pim)
            .Publishes(ProductFlow.Raw);

        s.AddTransactionalIntegration("pricing-service")
            .From(s_pim)
            .To(s_erp);

        s.AddLoader("product-loader")
            .Subscribes(ProductFlow.Raw)
            .To(s_erp);

        // An extractor whose source stays a private local component: no From edge.
        s.AddExtractor("price-extractor")
            .Publishes(PriceFlow.Raw);

        // A loader whose destination stays a private local component: no To edge.
        s.AddLoader("price-loader")
            .Subscribes(PriceFlow.Raw);

        return s;
    }

    [Fact]
    public void Build_WithSampleSystem_ShouldMaterializeFullTopology()
    {
        // Act
        var topology = DeclareSystem().Build();

        // Assert: system + components
        Assert.Equal("product-distribution", topology.SystemName);
        Assert.Equal(["pim-extractor", "pricing-service", "product-loader", "price-extractor", "price-loader"],
            topology.Components.Select(w => w.Name));

        var extractor = topology.Components[0];
        Assert.Equal(PortDirection.In, Assert.Single(extractor.Ports).Direction);
        Assert.Equal("product-raw", Assert.Single(extractor.Publishes).TopicName);

        var pricing = topology.Components[1];
        Assert.Equal(
            [("pim", PortDirection.In), ("erp", PortDirection.Out)],
            pricing.Ports.Select(c => (c.PortName, c.Direction)));

        var loader = topology.Components[2];
        Assert.Equal("product-raw", Assert.Single(loader.Subscribes).TopicName);
        Assert.Equal(("erp", PortDirection.Out),
            (Assert.Single(loader.Ports).PortName, Assert.Single(loader.Ports).Direction));

        // Assert: materialized resources
        Assert.Equal(["price-raw", "product-raw"], topology.Topics.Select(t => t.TopicName));
        var raw = topology.Topics.Single(t => t.TopicName == "product-raw");
        Assert.Equal(["pim-extractor"], raw.Publishers);
        Assert.Equal(["product-loader"], raw.Subscribers);
        var priceRaw = topology.Topics.Single(t => t.TopicName == "price-raw");
        Assert.Equal(["price-extractor"], priceRaw.Publishers);
        Assert.Equal(["price-loader"], priceRaw.Subscribers);

        Assert.Equal(["erp", "pim"], topology.Ports.Select(c => c.Name));
        var erp = topology.Ports[0];
        Assert.Equal("erp", erp.DaprComponentName);
        Assert.Equal(["pricing-service", "product-loader"], erp.UsedBy);
        var pim = topology.Ports[1];
        Assert.Equal("pim", pim.DaprComponentName);
        Assert.Equal(["pim-extractor", "pricing-service"], pim.UsedBy);
    }

    [Fact]
    public void Validate_WithSampleSystem_ShouldReportNothing()
    {
        // Act & Assert
        Assert.Empty(DeclareSystem().Validate());
    }

    [Fact]
    public void Serialize_WithSampleSystem_ShouldMatchSnapshot()
    {
        // Byte-exact snapshot of the sample system's serialized model: guards materializer
        // output against unintentional change. Regenerate only on intentional model changes.
        const string expected =
            """{"SystemName":"product-distribution","Components":[{"Name":"pim-extractor","Kind":0,"Subscribes":[],"Publishes":[{"PubSubName":"product-distribution-pubsub","TopicName":"product-raw"}],"Ports":[{"PortName":"pim","Direction":0}],"Uses":[],"InternalQueue":null},{"Name":"pricing-service","Kind":2,"Subscribes":[],"Publishes":[],"Ports":[{"PortName":"pim","Direction":0},{"PortName":"erp","Direction":1}],"Uses":[],"InternalQueue":{"PubSubName":"internal-pricing-service","TopicName":"hop"}},{"Name":"product-loader","Kind":1,"Subscribes":[{"PubSubName":"product-distribution-pubsub","TopicName":"product-raw"}],"Publishes":[],"Ports":[{"PortName":"erp","Direction":1}],"Uses":[],"InternalQueue":null},{"Name":"price-extractor","Kind":0,"Subscribes":[],"Publishes":[{"PubSubName":"product-distribution-pubsub","TopicName":"price-raw"}],"Ports":[],"Uses":[],"InternalQueue":null},{"Name":"price-loader","Kind":1,"Subscribes":[{"PubSubName":"product-distribution-pubsub","TopicName":"price-raw"}],"Publishes":[],"Ports":[],"Uses":[],"InternalQueue":null}],"Topics":[{"PubSubName":"product-distribution-pubsub","TopicName":"price-raw","ContractTypeName":"Intropy.Topology.Test.RawEvent","Publishers":["price-extractor"],"Subscribers":["price-loader"]},{"PubSubName":"product-distribution-pubsub","TopicName":"product-raw","ContractTypeName":"Intropy.Topology.Test.RawEvent","Publishers":["pim-extractor"],"Subscribers":["product-loader"]}],"Ports":[{"Name":"erp","DaprComponentName":"erp","Directions":[1],"UsedBy":["pricing-service","product-loader"]},{"Name":"pim","DaprComponentName":"pim","Directions":[0],"UsedBy":["pim-extractor","pricing-service"]}],"Services":[]}""";
        // Act
        var json = JsonSerializer.Serialize(DeclareSystem().Build());

        // Assert
        Assert.Equal(expected, json);
    }

    [Fact]
    public void Serialize_WithSampleSystem_ShouldRoundTrip()
    {
        // Arrange
        var topology = DeclareSystem().Build();

        // Act
        var json = JsonSerializer.Serialize(topology);
        var deserialized = JsonSerializer.Deserialize<SystemTopology>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(topology.SystemName, deserialized.SystemName);
        Assert.Equal(topology.Components.Count, deserialized.Components.Count);
        Assert.Equal(["product-raw"], deserialized.Components[2].Subscribes.Select(t => t.TopicName));
        Assert.Equal("erp", deserialized.Ports.Single(c => c.Name == "erp").DaprComponentName);
    }
}
