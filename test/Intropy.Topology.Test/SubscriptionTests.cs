using Intropy.Topology.Building;
using Intropy.Topology.Model;
using Intropy.Topology.Test.Validation;
using Intropy.Topology.Validation;
using Intropy.Topology.Validation.Rules;

namespace Intropy.Topology.Test;

public sealed record OrderPlaced;

public sealed record OrderCancelled;

/// <summary>
/// A subscription: the messages a component handles from one channel, and what happens to the
/// channel's other messages.
/// </summary>
public sealed class SubscriptionTests
{
    private static readonly MessageRef<OrderPlaced> s_placed =
        MessageRef<OrderPlaced>.Define("fluxia.orders.order-placed", "pubsub", "orders");

    private static readonly MessageRef<OrderCancelled> s_cancelled =
        MessageRef<OrderCancelled>.Define("fluxia.orders.order-cancelled", "pubsub", "orders");

    private static ComponentModel Loader(SystemTopology topology) =>
        topology.Components.Single(c => c.Name == "fulfillment");

    private static SystemBuilder System(Action<LoaderBuilder> loader)
    {
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("order-extractor").Publishes(s_placed).Publishes(s_cancelled);
        loader(builder.AddLoader("fulfillment"));
        return builder;
    }

    [Fact]
    public void Build_WithASubscriptionHandlingTwoMessages_ShouldMaterializeOneSubscriptionToTheirChannel()
    {
        // Arrange
        var builder = System(l => l.Subscribes(sub => sub.Handles(s_placed).Handles(s_cancelled)));

        // Act
        var topology = builder.Build();

        // Assert
        var subscription = Assert.Single(Loader(topology).Subscribes);
        Assert.Equal("pubsub", subscription.PubSubName);
        Assert.Equal("orders", subscription.TopicName);
        Assert.Equal(["fluxia.orders.order-placed", "fluxia.orders.order-cancelled"], subscription.Messages);
        Assert.Equal(UnhandledMessages.DeadLetter, subscription.Unhandled);
    }

    [Fact]
    public void Build_WithAContentFilter_ShouldMaterializeItForItsMessageOnly()
    {
        // Arrange
        var builder = System(l => l.Subscribes(sub => sub
            .Handles(s_placed)
            .Handles(s_cancelled, when: "  event.data.reason == 'customer-request'  ")));

        // Act
        var subscription = Assert.Single(Loader(builder.Build()).Subscribes);

        // Assert
        Assert.Equal("event.data.reason == 'customer-request'", subscription.ConditionFor(s_cancelled.Name));
        Assert.Null(subscription.ConditionFor(s_placed.Name));
    }

    [Fact]
    public void Build_WithoutContentFilters_ShouldMaterializeNoConditions()
    {
        // Act
        var subscription = Assert.Single(Loader(System(l => l.Subscribes(s_placed)).Build()).Subscribes);

        // Assert
        Assert.Null(subscription.Conditions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Handles_WithABlankContentFilter_ShouldThrow(string when)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => System(l => l.Subscribes(sub => sub.Handles(s_placed, when))));
    }

    [Fact]
    public void Build_WithIgnoreOthers_ShouldMaterializeTheChoice()
    {
        // Arrange
        var builder = System(l => l.Subscribes(sub => sub.Handles(s_placed).IgnoreOthers()));

        // Act
        var topology = builder.Build();

        // Assert
        Assert.Equal(UnhandledMessages.Ignore, Assert.Single(Loader(topology).Subscribes).Unhandled);
    }

    [Fact]
    public void Build_WithTheSingleMessageShorthand_ShouldMaterializeASubscriptionHandlingIt()
    {
        // Arrange
        var builder = System(l => l.Subscribes(s_placed));

        // Act
        var topology = builder.Build();

        // Assert
        Assert.Equal(["fluxia.orders.order-placed"], Assert.Single(Loader(topology).Subscribes).Messages);
    }

    [Fact]
    public void Build_WithInBatchesOnTheSubscription_ShouldMaterializeABulkSubscription()
    {
        // Arrange
        var builder = System(l => l.Subscribes(sub => sub.Handles(s_placed).InBatches(50, TimeSpan.FromMilliseconds(500))));

        // Act
        var topology = builder.Build();

        // Assert
        var bulk = Assert.Single(Loader(topology).Subscribes).Bulk;
        Assert.NotNull(bulk);
        Assert.Equal(50, bulk.MaxMessages);
    }

    [Fact]
    public void Build_WithoutInBatches_ShouldMaterializeOneMessageAtATime()
    {
        // Act
        var subscription = Assert.Single(Loader(System(l => l.Subscribes(s_placed)).Build()).Subscribes);

        // Assert
        Assert.Null(subscription.Bulk);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(10, 0)]
    public void InBatches_WithAnInvalidSetting_ShouldThrow(int maxMessages, int maxWaitMilliseconds)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            System(l => l.Subscribes(sub => sub.Handles(s_placed)
                .InBatches(maxMessages, TimeSpan.FromMilliseconds(maxWaitMilliseconds)))));
    }

    [Fact]
    public void InBatches_WithAFractionalMillisecondWait_ShouldThrow()
    {
        // Act & Assert — the sidecar's bulk wait is a whole number of milliseconds.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            System(l => l.Subscribes(sub => sub.Handles(s_placed).InBatches(10, TimeSpan.FromTicks(15_000)))));
    }

    [Fact]
    public void InBatches_DeclaredTwice_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => System(l => l.Subscribes(sub => sub
            .Handles(s_placed)
            .InBatches(10, TimeSpan.FromSeconds(1))
            .InBatches(20, TimeSpan.FromSeconds(1)))));
    }

    [Fact]
    public void Build_ShouldRecordEachHandledMessagesSubscriber()
    {
        // Arrange: the loader handles one of the channel's two messages
        var builder = System(l => l.Subscribes(sub => sub.Handles(s_placed).IgnoreOthers()));

        // Act
        var messages = builder.Build().MessageGroups.Single().Messages;

        // Assert
        Assert.Equal(["fulfillment"], messages.Single(m => m.Name == "fluxia.orders.order-placed").Subscribers);
        Assert.Empty(messages.Single(m => m.Name == "fluxia.orders.order-cancelled").Subscribers);
    }

    [Fact]
    public void Validate_WithMessagesFromDifferentChannels_ShouldReportError()
    {
        // Arrange
        var elsewhere = MessageRef<OrderCancelled>.Define("fluxia.returns.order-cancelled", "pubsub", "returns");
        var builder = System(l => l.Subscribes(sub => sub.Handles(s_placed).Handles(elsewhere)));

        // Act
        var diagnostic = Assert.Single(builder.DeclarationDiagnosticsFor<SubscriptionChannelConflictRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("fulfillment", diagnostic.Target);
        Assert.Contains("'orders' on pubsub 'pubsub'", diagnostic.Message);
        Assert.Contains("'returns' on pubsub 'pubsub'", diagnostic.Message);
    }

    [Fact]
    public void Validate_WithAnEmptySubscription_ShouldReportError()
    {
        // Arrange
        var builder = System(l => l.Subscribes(_ => { }));

        // Act
        var diagnostic = Assert.Single(builder.DeclarationDiagnosticsFor<EmptySubscriptionRule>());

        // Assert
        Assert.Equal("fulfillment", diagnostic.Target);
        Assert.Throws<TopologyValidationException>(() => builder.Build());
    }

    [Fact]
    public void Validate_WithAMessageHandledTwice_ShouldReportError()
    {
        // Arrange
        var builder = System(l => l.Subscribes(sub => sub.Handles(s_placed).Handles(s_placed)));

        // Act
        var diagnostic = Assert.Single(builder.DeclarationDiagnosticsFor<DuplicateHandledMessageRule>());

        // Assert
        Assert.Contains("fluxia.orders.order-placed", diagnostic.Message);
    }

    [Fact]
    public void Validate_WithTwoSubscriptions_ShouldReportThatALoaderSubscribesToExactlyOneTopic()
    {
        // Arrange: one channel per loader
        var elsewhere = MessageRef<OrderCancelled>.Define("fluxia.returns.order-cancelled", "pubsub", "returns");
        var builder = System(l => l.Subscribes(s_placed).Subscribes(elsewhere));

        // Act & Assert
        Assert.Single(builder.DiagnosticsFor<MissingRequiredSubscriptionRule>());
    }
}
