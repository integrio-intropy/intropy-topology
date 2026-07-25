using Intropy.Topology.Building;
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
        s.AddTransactionalIntegration("ti").From(TestConnectors.Pim);
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
        Assert.Equal(Transport.File("./test/pim"), connector.Transport);
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
            .From(TestConnectors.Pim);

        // Act
        var topology = s.Build();

        // Assert: one edge, one resource, no diagnostics
        Assert.Single(topology.Components.Single().Connectors);
        Assert.Single(topology.Connectors);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Build_ShouldSortResourcesDeterministically()
    {
        // Arrange: declare in non-alphabetical order
        var erp = ConnectorRef.Define("erp", Transport.File("./test/erp"));
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

    [Fact]
    public void Build_WithDuplicateIdenticalServiceDeclarations_ShouldDedupeSilently()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);
        s.AddLoader("sink").Subscribes(TestTopics.Raw);
        s.UsesService(TestServices.Idempotency);
        s.UsesService(TestServices.Idempotency);

        // Act
        var topology = s.Build();

        // Assert: one resource, no diagnostics
        Assert.Single(topology.Services);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Build_WithConflictingServiceDeclarations_ShouldKeepFirstSeen()
    {
        // Arrange: materialization never fails; the conflict is validation's to report
        var s = SystemBuilder.Create("test-system");
        s.UsesService(ServiceRef.Define("idempotency", Service.Container("redis:7", 6379)));
        s.UsesService(ServiceRef.Define("idempotency", Service.Container("redis:8", 6379)));

        // Act
        var topology = TopologyMaterializer.Materialize(s);

        // Assert
        var service = Assert.Single(topology.Services);
        Assert.Equal(Service.Container("redis:7", 6379), service.Service);
    }

    [Fact]
    public void Build_ShouldSortServicesDeterministically()
    {
        // Arrange: declare in non-alphabetical order
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);
        s.AddLoader("sink").Subscribes(TestTopics.Raw);
        s.UsesService(ServiceRef.Define("zeta-store", Service.Container("redis:7", 6380)));
        s.UsesService(ServiceRef.Define("alpha-store", Service.Container("redis:7", 6379)));

        // Act & Assert
        Assert.Equal(["alpha-store", "zeta-store"], s.Build().Services.Select(x => x.Name));
    }

    // A second edge that a block cannot legally have (formerly the ITP203/204/205 family)
    // is a compile error: the block builders expose only that block's legal edge methods.
}
