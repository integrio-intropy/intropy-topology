using Intropy.Topology.Building;
using Intropy.Topology.Model;

namespace Intropy.Topology.Test.Building;

public class BuilderMappingTests
{
    [Fact]
    public void AddComponent_PerKind_ShouldMapComponentKindAndDeclarationOrder()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("a").Publishes(TestMessages.Raw);
        s.AddLoader("d").Subscribes(TestMessages.Raw).To(TestPorts.Erp);
        s.AddTransactionalIntegration("e").From(TestPorts.Pim).To(TestPorts.Erp);

        // Act
        var topology = s.Build();

        // Assert
        Assert.Equal(["a", "d", "e"], topology.Components.Select(w => w.Name));
        Assert.Equal(
            [
                ComponentKind.Extractor,
                ComponentKind.Loader,
                ComponentKind.TransactionalIntegration,
            ],
            topology.Components.Select(w => w.Kind));
    }

    [Fact]
    public void Subscribes_OnLoader_ShouldRecordSingleSubscription()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").Subscribes(TestMessages.Raw).To(TestPorts.Erp);
        s.AddExtractor("source").Publishes(TestMessages.Raw);

        // Act
        var component = s.Build().Components[0];

        // Assert
        var subscription = Assert.Single(component.Subscribes);
        Assert.Equal("test-pubsub", subscription.PubSubName);
        Assert.Equal("raw-events", subscription.TopicName);
    }

    [Fact]
    public void From_ShouldRecordInEdge()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").From(TestPorts.Pim).Publishes(TestMessages.Raw);
        s.AddLoader("sink").Subscribes(TestMessages.Raw);

        // Act
        var component = s.Build().Components[0];

        // Assert
        var edge = Assert.Single(component.Ports);
        Assert.Equal("pim", edge.PortName);
        Assert.Equal(PortDirection.In, edge.Direction);
    }

    [Fact]
    public void Publishes_ShouldRecordTopic()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestMessages.Raw);
        s.AddLoader("sink").Subscribes(TestMessages.Raw);

        // Act
        var edge = s.Build().Components[0].Publishes.Single();

        // Assert
        Assert.Equal("test-pubsub", edge.PubSubName);
        Assert.Equal("raw-events", edge.TopicName);
    }

    [Fact]
    public void Publishes_OnLoader_ShouldRecordTopic()
    {
        // Arrange: the receipt shape — the loader publishes a different message than it consumes,
        // for another component to pick up
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").Subscribes(TestMessages.Raw).Publishes(TestMessages.Enriched);
        s.AddExtractor("source").Publishes(TestMessages.Raw);
        s.AddLoader("receipt-sink").Subscribes(TestMessages.Enriched);

        // Act
        var edge = s.Build().Components[0].Publishes.Single();

        // Assert
        Assert.Equal("test-pubsub", edge.PubSubName);
        Assert.Equal("enriched-events", edge.TopicName);
    }

    [Fact]
    public void PublishAndSubscribe_DifferentMessages_ShouldMaterializeBothSidesOfEachTopic()
    {
        // Arrange: the receipt shape end to end — an extractor feeds the loader, and the loader's
        // receipt is consumed by a different component
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("source").Publishes(TestMessages.Raw);
        s.AddLoader("loader").Subscribes(TestMessages.Raw).Publishes(TestMessages.Enriched);
        s.AddLoader("receipt-sink").Subscribes(TestMessages.Enriched);

        // Act
        var topology = s.Build();

        // Assert
        var raw = topology.Topics.Single(t => t.TopicName == "raw-events");
        Assert.Equal(["source"], raw.Publishers);
        Assert.Equal(["loader"], raw.Subscribers);
        var enriched = topology.Topics.Single(t => t.TopicName == "enriched-events");
        Assert.Equal(["loader"], enriched.Publishers);
        Assert.Equal(["receipt-sink"], enriched.Subscribers);
    }

    [Fact]
    public void PublishAndSubscribe_SameMessageOnOneChannel_ShouldMaterializeBothEdgesWithoutDuplication()
    {
        // Arrange: the self-trigger pattern — a loader subscribes to and publishes the same
        // message on its one channel, so the topic sees it as both publisher and subscriber
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").Subscribes(TestMessages.Raw).Publishes(TestMessages.Raw);

        // Act
        var topology = s.Build();

        // Assert
        var topic = Assert.Single(topology.Topics);
        Assert.Equal(["loader"], topic.Publishers);
        Assert.Equal(["loader"], topic.Subscribers);
    }

    [Fact]
    public void FromAndTo_ShouldMapDirections()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti")
            .From(TestPorts.Pim)
            .To(TestPorts.Erp);

        // Act
        var component = s.Build().Components.Single();

        // Assert
        Assert.Equal(
            [("pim", PortDirection.In), ("erp", PortDirection.Out)],
            component.Ports.Select(c => (c.PortName, c.Direction)));
    }

    [Fact]
    public void FluentMethods_ShouldReturnSameBuilderInstance()
    {
        // Arrange: every edge call chains on the one builder Add* returned
        var s = SystemBuilder.Create("test-system");
        var builder = s.AddExtractor("extractor");

        // Assert
        Assert.Same(builder, builder.Publishes(TestMessages.Raw));
        Assert.Same(builder, builder.From(TestPorts.Pim));
    }

    [Fact]
    public void LoaderFluentMethods_ShouldReturnSameBuilderInstance()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        var builder = s.AddLoader("loader");

        // Assert
        Assert.Same(builder, builder.Subscribes(TestMessages.Raw));
        Assert.Same(builder, builder.Publishes(TestMessages.Raw));
        Assert.Same(builder, builder.To(TestPorts.Erp));
    }

    [Fact]
    public void Calls_ShouldChainWithKindSpecificEdgesOnEveryBuilderKind()
    {
        // Arrange: Calls lives on the shared base and must chain with each kind's own edges
        var service = ServiceRef.Define("idempotency-service");
        var s = SystemBuilder.Create("test-system");

        // Act
        var extractor = s.AddExtractor("extractor").Calls(service).Publishes(TestMessages.Raw);
        var loader = s.AddLoader("loader").Subscribes(TestMessages.Raw).Calls(service).To(TestPorts.Erp);
        var integration = s.AddTransactionalIntegration("ti").From(TestPorts.Pim).Calls(service).To(TestPorts.Erp);
        var topology = s.Build();

        // Assert
        Assert.IsType<ExtractorBuilder>(extractor);
        Assert.IsType<LoaderBuilder>(loader);
        Assert.IsType<TransactionalIntegrationBuilder>(integration);
        Assert.All(topology.Components, component => Assert.Equal(["idempotency-service"], component.Uses));
    }

    [Fact]
    public void Build_CalledTwiceAfterMutation_ShouldReflectMutation()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestMessages.Raw);
        s.AddLoader("loader").Subscribes(TestMessages.Raw).To(TestPorts.Erp);
        var first = s.Build();

        // Act
        s.AddLoader("audit").Subscribes(TestMessages.Raw);
        var second = s.Build();

        // Assert
        Assert.Equal(2, first.Components.Count);
        Assert.Equal(3, second.Components.Count);
    }

    [Theory]
    [InlineData("Bad-Name")]
    [InlineData("bad_name")]
    [InlineData("")]
    public void AddExtractor_WithInvalidName_ShouldThrowArgumentException(string name)
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => s.AddExtractor(name));
    }
}

public class GatewayBuilderTests
{
    private static readonly EndpointRef s_orders = EndpointRef.Define("order-gateway-endpoint", "/orders");

    [Fact]
    public void AddGateway_ShouldMaterializeKindEndpointsAndPublishes()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddGateway("order-gateway").Exposes(s_orders).Publishes(TestMessages.Raw);
        s.AddLoader("sink").Subscribes(TestMessages.Raw);

        // Act
        var topology = s.Build();
        var gateway = topology.Components[0];

        // Assert
        Assert.Equal(ComponentKind.Gateway, gateway.Kind);
        var endpoint = Assert.Single(gateway.Endpoints!);
        Assert.Equal(("order-gateway-endpoint", "/orders"), (endpoint.EndpointName, endpoint.Path));
        Assert.Equal("raw-events", Assert.Single(gateway.Publishes).TopicName);
        Assert.Equal(new ComponentChannel("test-pubsub", "raw-events"), ComponentChannel.For(gateway));
        // An endpoint is served over HTTP, never a Dapr binding, so it is no port.
        Assert.Empty(gateway.Ports);
        Assert.Empty(topology.Ports);
        Assert.Null(topology.Components[1].Endpoints);
    }

    [Fact]
    public void Exposes_WithSameEndpointTwice_ShouldThrow()
    {
        // Arrange
        var gateway = SystemBuilder.Create("test-system").AddGateway("order-gateway").Exposes(s_orders);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => gateway.Exposes(s_orders));
    }

    [Fact]
    public void Exposes_WithAPathAlreadyServed_ShouldThrow()
    {
        // Arrange
        var gateway = SystemBuilder.Create("test-system").AddGateway("order-gateway").Exposes(s_orders);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => gateway.Exposes(EndpointRef.Define("other-endpoint", "/orders")));
    }
}

public class JobBuilderTests
{
    [Fact]
    public void AddJob_ShouldMaterializeKindWithoutEdges()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddJob("nightly-cleanup").Calls(ServiceRef.Define("business-incident-service"));

        // Act
        var topology = s.Build();
        var job = Assert.Single(topology.Components);

        // Assert
        Assert.Equal(ComponentKind.Job, job.Kind);
        Assert.Equal(["business-incident-service"], job.Uses);
        Assert.Empty(job.Subscribes);
        Assert.Empty(job.Publishes);
        Assert.Empty(job.Ports);
        Assert.Null(job.Endpoints);
        Assert.Null(job.InternalQueue);
        // A job runs on no channel: the hosts pass it neither INTROPY_PUBSUB_NAME nor INTROPY_TOPIC_NAME.
        Assert.Equal(new ComponentChannel(null, null), ComponentChannel.For(job));
    }

    [Fact]
    public void AddJob_WithInvalidName_ShouldThrow()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => s.AddJob("Nightly_Cleanup"));
    }
}
