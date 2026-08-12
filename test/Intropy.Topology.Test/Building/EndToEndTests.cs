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

    private static readonly ConnectorRef s_pim = ConnectorRef.Define("pim");
    private static readonly ConnectorRef s_erp = ConnectorRef.Define("erp");

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
        Assert.Equal(ConnectorDirection.In, Assert.Single(extractor.Connectors).Direction);
        Assert.Equal("default", Assert.Single(extractor.Publishes).Port);

        var pricing = topology.Components[1];
        Assert.Equal(
            [("pim", ConnectorDirection.In), ("erp", ConnectorDirection.Out)],
            pricing.Connectors.Select(c => (c.ConnectorName, c.Direction)));

        var loader = topology.Components[2];
        Assert.Equal("product-raw", Assert.Single(loader.Subscribes).TopicName);
        Assert.Equal(("erp", ConnectorDirection.Out),
            (Assert.Single(loader.Connectors).ConnectorName, Assert.Single(loader.Connectors).Direction));

        // Assert: materialized resources
        Assert.Equal(["price-raw", "product-raw"], topology.Topics.Select(t => t.TopicName));
        var raw = topology.Topics.Single(t => t.TopicName == "product-raw");
        Assert.Equal(["pim-extractor"], raw.Publishers);
        Assert.Equal(["product-loader"], raw.Subscribers);
        var priceRaw = topology.Topics.Single(t => t.TopicName == "price-raw");
        Assert.Equal(["price-extractor"], priceRaw.Publishers);
        Assert.Equal(["price-loader"], priceRaw.Subscribers);

        Assert.Equal(["erp", "pim"], topology.Connectors.Select(c => c.Name));
        var erp = topology.Connectors[0];
        Assert.Equal("binding.erp", erp.DaprComponentName);
        Assert.Equal(["pricing-service", "product-loader"], erp.UsedBy);
        var pim = topology.Connectors[1];
        Assert.Equal("binding.pim", pim.DaprComponentName);
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
            """{"SystemName":"product-distribution","Components":[{"Name":"pim-extractor","Kind":0,"Subscribes":[],"Publishes":[{"Port":"default","PubSubName":"product-distribution-pubsub","TopicName":"product-raw"}],"Connectors":[{"ConnectorName":"pim","Direction":0}],"Uses":[],"InternalQueue":null},{"Name":"pricing-service","Kind":2,"Subscribes":[],"Publishes":[],"Connectors":[{"ConnectorName":"pim","Direction":0},{"ConnectorName":"erp","Direction":1}],"Uses":[],"InternalQueue":{"PubSubName":"internal-pricing-service","TopicName":"hop"}},{"Name":"product-loader","Kind":1,"Subscribes":[{"PubSubName":"product-distribution-pubsub","TopicName":"product-raw"}],"Publishes":[],"Connectors":[{"ConnectorName":"erp","Direction":1}],"Uses":[],"InternalQueue":null},{"Name":"price-extractor","Kind":0,"Subscribes":[],"Publishes":[{"Port":"default","PubSubName":"product-distribution-pubsub","TopicName":"price-raw"}],"Connectors":[],"Uses":[],"InternalQueue":null},{"Name":"price-loader","Kind":1,"Subscribes":[{"PubSubName":"product-distribution-pubsub","TopicName":"price-raw"}],"Publishes":[],"Connectors":[],"Uses":[],"InternalQueue":null}],"Topics":[{"PubSubName":"product-distribution-pubsub","TopicName":"price-raw","ContractTypeName":"Intropy.Topology.Test.RawEvent","Publishers":["price-extractor"],"Subscribers":["price-loader"]},{"PubSubName":"product-distribution-pubsub","TopicName":"product-raw","ContractTypeName":"Intropy.Topology.Test.RawEvent","Publishers":["pim-extractor"],"Subscribers":["product-loader"]}],"Connectors":[{"Name":"erp","DaprComponentName":"binding.erp","Directions":[1],"UsedBy":["pricing-service","product-loader"]},{"Name":"pim","DaprComponentName":"binding.pim","Directions":[0],"UsedBy":["pim-extractor","pricing-service"]}],"Services":[]}""";
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
        Assert.Equal("binding.erp", deserialized.Connectors.Single(c => c.Name == "erp").DaprComponentName);
    }
}
