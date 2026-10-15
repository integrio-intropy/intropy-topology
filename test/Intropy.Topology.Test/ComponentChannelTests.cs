using Intropy.Topology.Model;

namespace Intropy.Topology.Test;

public class ComponentChannelTests
{
    private static readonly MessageRef<EnrichedEvent> s_systemEvents = MessageRef<EnrichedEvent>.Define("system-events");

    private static ComponentModel Built(SystemBuilder s, string name) =>
        s.Build().Components.Single(c => c.Name == name);

    [Fact]
    public void For_WithAnExtractorPublishingOneMessage_ShouldBeItsChannel()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("pim-extractor").From(TestPorts.Pim).Publishes(s_systemEvents);
        s.AddLoader("sink").Subscribes(s_systemEvents);

        // Act
        var channel = ComponentChannel.For(Built(s, "pim-extractor"));

        // Assert — a message naming no pubsub rides the system's own
        Assert.Equal(new ComponentChannel("test-system-pubsub", "system-events"), channel);
    }

    [Fact]
    public void For_WithALoaderSubscribingOneMessage_ShouldBeItsChannel()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("source").Publishes(TestMessages.Raw);
        s.AddLoader("erp-loader").Subscribes(TestMessages.Raw).To(TestPorts.Erp);

        // Act
        var channel = ComponentChannel.For(Built(s, "erp-loader"));

        // Assert
        Assert.Equal(new ComponentChannel("test-pubsub", "raw-events"), channel);
    }

    [Fact]
    public void For_WithATransactionalIntegration_ShouldBeItsInternalQueue()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("price-sync").From(TestPorts.Pim).To(TestPorts.Erp);

        // Act
        var channel = ComponentChannel.For(Built(s, "price-sync"));

        // Assert
        Assert.Equal(new ComponentChannel("test-system-pubsub", "internal-price-sync"), channel);
    }

    [Fact]
    public void For_WithSeveralTopicsOnOnePubSub_ShouldLeaveTheTopicUnset()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("pim-extractor").Publishes(TestMessages.Raw).Publishes(TestMessages.Enriched);
        s.AddLoader("raw-sink").Subscribes(TestMessages.Raw);
        s.AddLoader("enriched-sink").Subscribes(TestMessages.Enriched);

        // Act
        var channel = ComponentChannel.For(Built(s, "pim-extractor"));

        // Assert
        Assert.Equal(new ComponentChannel("test-pubsub", null), channel);
    }

    [Fact]
    public void For_WithChannelsOnSeveralPubSubs_ShouldLeaveBothUnset()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("pim-extractor").Publishes(TestMessages.Raw).Publishes(s_systemEvents);
        s.AddLoader("sink").Subscribes(TestMessages.Raw);
        s.AddLoader("system-sink").Subscribes(s_systemEvents);

        // Act
        var channel = ComponentChannel.For(Built(s, "pim-extractor"));

        // Assert
        Assert.Equal(new ComponentChannel(null, null), channel);
    }
}
