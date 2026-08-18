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
        s.AddTransactionalIntegration("ti").From(TestPorts.Pim).To(TestPorts.Erp);
        s.AddLoader("loader").Subscribes(TestTopics.Raw).To(TestPorts.Erp);

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
    public void Build_WithSamePortInBothDirections_ShouldMaterializeOneResourceWithBothDirections()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti")
            .From(TestPorts.Pim)
            .To(TestPorts.Pim);

        // Act
        var port = s.Build().Ports.Single();

        // Assert
        Assert.Equal("pim", port.Name);
        Assert.Equal("pim", port.DaprComponentName);
        Assert.Equal([PortDirection.In, PortDirection.Out], port.Directions);
        Assert.Equal(["ti"], port.UsedBy);
    }

    [Fact]
    public void Build_WithDuplicateSameDirectionPortCalls_ShouldDedupeSilently()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti")
            .From(TestPorts.Pim)
            .From(TestPorts.Pim)
            .To(TestPorts.Erp);

        // Act
        var topology = s.Build();

        // Assert: the duplicate From collapsed to one edge; the declared To stays.
        Assert.Equal(
            [("pim", PortDirection.In), ("erp", PortDirection.Out)],
            topology.Components.Single().Ports.Select(c => (c.PortName, c.Direction)));
        Assert.Equal(2, topology.Ports.Count);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Build_ShouldSortResourcesDeterministically()
    {
        // Arrange: declare in non-alphabetical order
        var erp = PortRef.Define("erp");
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("zeta")
            .Publishes(TopicRef<RawEvent>.Define("test-pubsub", "zzz-topic"))
            .From(TestPorts.Pim);
        s.AddExtractor("alpha")
            .Publishes(TopicRef<RawEvent>.Define("test-pubsub", "aaa-topic"))
            .From(erp);
        s.AddLoader("sink-a").Subscribes(TopicRef<RawEvent>.Define("test-pubsub", "aaa-topic"));
        s.AddLoader("sink-z").Subscribes(TopicRef<RawEvent>.Define("test-pubsub", "zzz-topic"));

        // Act
        var topology = s.Build();

        // Assert
        Assert.Equal(["aaa-topic", "zzz-topic"], topology.Topics.Select(t => t.TopicName));
        Assert.Equal(["erp", "pim"], topology.Ports.Select(c => c.Name));
    }

    [Fact]
    public void Build_ShouldMint_InternalQueueForTransactionalIntegrations()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("price-sync").From(TestPorts.Pim).To(TestPorts.Erp);
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);
        s.AddLoader("loader").Subscribes(TestTopics.Raw);

        // Act
        var topology = s.Build();

        // Assert
        var ti = topology.Components.Single(c => c.Name == "price-sync");
        Assert.Equal("internal-price-sync", ti.InternalQueue?.PubSubName);
        Assert.Equal("hop", ti.InternalQueue?.TopicName);
        Assert.Null(topology.Components.Single(c => c.Name == "extractor").InternalQueue);
        Assert.Null(topology.Components.Single(c => c.Name == "loader").InternalQueue);

        // The hop is workload shape, not an inter-component edge: it never enters Topics.
        Assert.DoesNotContain(topology.Topics, t => t.PubSubName == "internal-price-sync");
    }

    // A second edge that a block cannot legally have is a compile error: the block
    // builders expose only that block's legal edge methods.
}
