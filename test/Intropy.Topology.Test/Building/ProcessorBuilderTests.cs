using Intropy.Topology.Model;

namespace Intropy.Topology.Test.Building;

public sealed record OrderTotalChanged;

public sealed record OrderClosedOut;

public class ProcessorBuilderTests
{
    private static readonly MessageRef<OrderTotalChanged> s_totals =
        MessageRef<OrderTotalChanged>.Define("order-total-changed", pubSub: "test-pubsub", topic: "order-totals");

    private static readonly MessageRef<OrderClosedOut> s_closed =
        MessageRef<OrderClosedOut>.Define("order-closed", pubSub: "test-pubsub", topic: "order-totals");

    [Fact]
    public void AddProcessor_ShouldMaterializeKindEdgesAndMintedState()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("source").Publishes(TestMessages.Raw);
        s.AddProcessor("order-totals").Subscribes(TestMessages.Raw).Publishes(s_totals);
        s.AddLoader("sink").Subscribes(s_totals);

        // Act
        var topology = s.Build();
        var processor = topology.Components.Single(c => c.Name == "order-totals");

        // Assert
        Assert.Equal(ComponentKind.Processor, processor.Kind);
        Assert.Equal("raw-events", Assert.Single(processor.Subscribes).TopicName);
        Assert.Equal("order-totals", Assert.Single(processor.Publishes).TopicName);
        var state = processor.State!;
        Assert.Equal("order-totals-state", state.StateStoreName);
        Assert.Equal("order-totals-state-outbox", state.OutboxStateStoreName);
        // The outbox publishes where the processor publishes.
        Assert.Equal(("test-pubsub", "order-totals"), (state.Outbox!.PubSubName, state.Outbox.TopicName));
        // It consumes one topic and publishes another, so no single topic is its channel: the
        // framework takes the input from the rendered Subscription.
        Assert.Equal(new ComponentChannel("test-pubsub", null), ComponentChannel.For(processor));
        Assert.All(topology.Components.Where(c => c.Kind is not ComponentKind.Processor), c => Assert.Null(c.State));
    }

    [Fact]
    public void Publishes_WithSeveralMessagesOnOneChannel_ShouldRecordEach()
    {
        // Arrange
        var processor = SystemBuilder.Create("test-system").AddProcessor("order-totals");

        // Act
        processor.Subscribes(TestMessages.Raw).Publishes(s_totals).Publishes(s_closed);

        // Assert
        Assert.Equal(2, processor.Component.PublishCalls.Count);
    }

    [Fact]
    public void Publishes_OnASecondChannel_ShouldThrowNamingTheOutbox()
    {
        // Arrange
        var processor = SystemBuilder.Create("test-system").AddProcessor("order-totals").Publishes(s_totals);

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => processor.Publishes(TestMessages.Enriched));

        // Assert
        Assert.Contains("Processor 'order-totals' publishes on different channels", error.Message);
        Assert.Contains("one outbox", error.Message);
    }

    [Fact]
    public void Subscribes_Twice_ShouldThrow()
    {
        // Arrange
        var processor = SystemBuilder.Create("test-system").AddProcessor("order-totals").Subscribes(TestMessages.Raw);

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => processor.Subscribes(TestMessages.Enriched));

        // Assert
        Assert.Contains("a processor subscribes to one channel", error.Message);
    }

    [Fact]
    public void AlsoHandles_WithAMessageOnAnotherChannel_ShouldThrowNamingTheProcessor()
    {
        // Arrange
        var processor = SystemBuilder.Create("test-system").AddProcessor("order-totals");

        // Act
        var error = Assert.Throws<InvalidOperationException>(() =>
            processor.Subscribes(TestMessages.Raw, configure: sub => sub.AlsoHandles(s_totals)));

        // Assert
        Assert.StartsWith("Processor 'order-totals' subscribes to messages on different channels", error.Message);
    }

    [Fact]
    public void Subscribes_WithIgnoreOthers_ShouldRecordIt()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddProcessor("order-totals").Subscribes(TestMessages.Raw, configure: sub => sub.IgnoreOthers()).Publishes(s_totals);

        // Act
        var subscription = Assert.Single(TopologyMaterializerFor(s).Components[0].Subscribes);

        // Assert
        Assert.Equal(UnhandledMessages.Ignore, subscription.Unhandled);
        Assert.Null(subscription.Bulk);
    }

    [Fact]
    public void LooksUp_ShouldRecordALookupPort()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddProcessor("order-totals").Subscribes(TestMessages.Raw).Publishes(s_totals).LooksUp(TestPorts.Pim);

        // Act
        var processor = TopologyMaterializerFor(s).Components[0];

        // Assert
        var port = Assert.Single(processor.Ports);
        Assert.Equal(("pim", PortDirection.Lookup), (port.PortName, port.Direction));
    }

    [Fact]
    public void Materialize_WithoutPublications_ShouldMintStateWithoutAnOutboxChannel()
    {
        // Arrange — incomplete, which validation reports; materialization must not fail.
        var s = SystemBuilder.Create("test-system");
        s.AddProcessor("order-totals").Subscribes(TestMessages.Raw);

        // Act
        var state = TopologyMaterializerFor(s).Components[0].State!;

        // Assert
        Assert.Null(state.Outbox);
        Assert.Equal("order-totals-state", state.StateStoreName);
    }

    private static SystemTopology TopologyMaterializerFor(SystemBuilder s) =>
        Intropy.Topology.Building.TopologyMaterializer.Materialize(s);
}
