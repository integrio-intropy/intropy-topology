using Intropy.Topology.Model;

namespace Intropy.Topology.Test.Building;

public class MaterializationTests
{
    [Fact]
    public void Build_ShouldPreserveComponentDeclarationOrder()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);
        s.AddTransactionalIntegration("ti").From(TestConnectors.Pim).To(TestConnectors.Erp);
        s.AddLoader("loader").Subscribes(TestTopics.Raw).To(TestConnectors.Erp);

        // Act
        var topology = s.Build();

        // Assert
        Assert.Equal(["extractor", "ti", "loader"], topology.Components.Select(c => c.Name));
    }

    [Fact]
    public void Build_WithSharedTopic_ShouldMaterializeOneTopicResourceWithBothSides()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);
        s.AddLoader("sink").Subscribes(TestTopics.Raw);

        // Act
        var topology = s.Build();

        // Assert
        var raw = Assert.Single(topology.Topics);
        Assert.Equal("test-pubsub", raw.PubSubName);
        Assert.Equal("raw-events", raw.TopicName);
        Assert.Equal(typeof(RawEvent).FullName, raw.ContractTypeName);
        Assert.Equal(["extractor"], raw.Publishers);
        Assert.Equal(["sink"], raw.Subscribers);
    }

    [Fact]
    public void Build_WithSameConnectorInBothDirections_ShouldMaterializeOneResourceWithBothDirections()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti")
            .From(TestConnectors.Pim)
            .To(TestConnectors.Pim);

        // Act
        var connector = s.Build().Connectors.Single();

        // Assert
        Assert.Equal("pim", connector.Name);
        Assert.Equal("binding.pim", connector.DaprComponentName);
        Assert.Equal([ConnectorDirection.In, ConnectorDirection.Out], connector.Directions);
        Assert.Equal(["ti"], connector.UsedBy);
    }

    [Fact]
    public void Build_WithDuplicateSameDirectionConnectorCalls_ShouldDedupeSilently()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti")
            .From(TestConnectors.Pim)
            .From(TestConnectors.Pim)
            .To(TestConnectors.Erp);

        // Act
        var topology = s.Build();

        // Assert: the duplicate From collapsed to one edge; the declared To stays.
        Assert.Equal(
            [("pim", ConnectorDirection.In), ("erp", ConnectorDirection.Out)],
            topology.Components.Single().Connectors.Select(c => (c.ConnectorName, c.Direction)));
        Assert.Equal(2, topology.Connectors.Count);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Build_ShouldSortResourcesDeterministically()
    {
        // Arrange: declare in non-alphabetical order
        var erp = ConnectorRef.Define("erp");
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("zeta")
            .Publishes(TopicRef<RawEvent>.Define("test-pubsub", "zzz-topic"))
            .From(TestConnectors.Pim);
        s.AddExtractor("alpha")
            .Publishes(TopicRef<RawEvent>.Define("test-pubsub", "aaa-topic"))
            .From(erp);
        s.AddLoader("sink-a").Subscribes(TopicRef<RawEvent>.Define("test-pubsub", "aaa-topic"));
        s.AddLoader("sink-z").Subscribes(TopicRef<RawEvent>.Define("test-pubsub", "zzz-topic"));

        // Act
        var topology = s.Build();

        // Assert
        Assert.Equal(["aaa-topic", "zzz-topic"], topology.Topics.Select(t => t.TopicName));
        Assert.Equal(["erp", "pim"], topology.Connectors.Select(c => c.Name));
    }

    // A second edge that a block cannot legally have is a compile error: the block
    // builders expose only that block's legal edge methods.
}
