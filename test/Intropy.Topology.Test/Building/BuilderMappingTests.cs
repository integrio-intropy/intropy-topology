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
