using Intropy.Topology.Model;

namespace Intropy.Topology.Test.Building;

public class BuilderMappingTests
{
    [Fact]
    public void AddComponent_PerKind_ShouldMapComponentKindAndDeclarationOrder()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("a").Publishes(TestTopics.Raw);
        s.AddLoader("d").Subscribes(TestTopics.Raw).To(TestConnectors.Erp);
        s.AddTransactionalIntegration("e").From(TestConnectors.Pim);

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
        s.AddLoader("loader").Subscribes(TestTopics.Raw).To(TestConnectors.Erp);
        s.AddExtractor("source").Publishes(TestTopics.Raw);

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
        s.AddExtractor("extractor").From(TestConnectors.Pim).Publishes(TestTopics.Raw);
        s.AddLoader("sink").Subscribes(TestTopics.Raw);

        // Act
        var component = s.Build().Components[0];

        // Assert
        var edge = Assert.Single(component.Connectors);
        Assert.Equal("pim", edge.ConnectorName);
        Assert.Equal(ConnectorDirection.In, edge.Direction);
    }

    [Fact]
    public void Publishes_ShouldUseDefaultPort()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);
        s.AddLoader("sink").Subscribes(TestTopics.Raw);

        // Act
        var edge = s.Build().Components[0].Publishes.Single();

        // Assert
        Assert.Equal("default", edge.Port);
        Assert.Equal("test-pubsub", edge.PubSubName);
        Assert.Equal("raw-events", edge.TopicName);
    }

    [Fact]
    public void FromAndTo_ShouldMapDirections()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti")
            .From(TestConnectors.Pim)
            .To(TestConnectors.Erp);

        // Act
        var component = s.Build().Components.Single();

        // Assert
        Assert.Equal(
            [("pim", ConnectorDirection.In), ("erp", ConnectorDirection.Out)],
            component.Connectors.Select(c => (c.ConnectorName, c.Direction)));
    }

    [Fact]
    public void FluentMethods_ShouldReturnSameBuilderInstance()
    {
        // Arrange: every edge call chains on the one builder Add* returned
        var s = SystemBuilder.Create("test-system");
        var builder = s.AddExtractor("extractor");

        // Assert
        Assert.Same(builder, builder.Publishes(TestTopics.Raw));
        Assert.Same(builder, builder.From(TestConnectors.Pim));
        Assert.Same(builder, builder.WithSchedule("@daily"));
    }

    [Fact]
    public void WithSchedule_ShouldRecordScheduleOnComponentModel()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw).WithSchedule("*/5 * * * *");
        s.AddLoader("sink").Subscribes(TestTopics.Raw);

        // Act
        var topology = s.Build();

        // Assert
        Assert.Equal("*/5 * * * *", topology.Components[0].Schedule);
        Assert.Null(topology.Components[1].Schedule);
    }

    [Fact]
    public void WithSchedule_CalledTwice_ShouldKeepLastValue()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .Publishes(TestTopics.Raw)
            .WithSchedule("@hourly")
            .WithSchedule("@daily");
        s.AddLoader("sink").Subscribes(TestTopics.Raw);

        // Act & Assert
        Assert.Equal("@daily", s.Build().Components[0].Schedule);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithSchedule_WithNullOrWhitespace_ShouldThrowArgumentException(string? cron)
    {
        // Arrange: null/whitespace is an argument error at the call site; cron *syntax*
        // is deferred to Build() (ITP210) so all diagnostics report at once.
        var s = SystemBuilder.Create("test-system");
        var builder = s.AddExtractor("extractor");

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => builder.WithSchedule(cron!));
    }

    [Fact]
    public void Build_CalledTwiceAfterMutation_ShouldReflectMutation()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);
        s.AddLoader("loader").Subscribes(TestTopics.Raw).To(TestConnectors.Erp);
        var first = s.Build();

        // Act
        s.AddLoader("audit").Subscribes(TestTopics.Raw);
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
