namespace Intropy.Topology.Test;

public sealed class LoaderBatchingTests
{
    private static readonly MessageRef<string> s_topic = MessageRef<string>.Define("product-changed", "catalog");

    [Fact]
    public void Build_WithInBatches_ShouldMaterializeABulkSubscription()
    {
        // Arrange
        var builder = SystemBuilder.Create("catalog");
        builder.AddExtractor("extractor").Publishes(s_topic);
        builder.AddLoader("loader").Subscribes(s_topic).InBatches(200, TimeSpan.FromSeconds(2));

        // Act
        var topology = builder.Build();

        // Assert
        var subscription = Assert.Single(topology.Components.Single(c => c.Name == "loader").Subscribes);
        Assert.NotNull(subscription.Bulk);
        Assert.Equal(200, subscription.Bulk.MaxMessages);
        Assert.Equal(TimeSpan.FromSeconds(2), subscription.Bulk.MaxWait);
    }

    [Fact]
    public void Build_WithoutInBatches_ShouldMaterializeOneMessageAtATime()
    {
        // Arrange
        var builder = SystemBuilder.Create("catalog");
        builder.AddExtractor("extractor").Publishes(s_topic);
        builder.AddLoader("loader").Subscribes(s_topic);

        // Act
        var topology = builder.Build();

        // Assert
        Assert.Null(Assert.Single(topology.Components.Single(c => c.Name == "loader").Subscribes).Bulk);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(10, 0)]
    public void InBatches_WithAnInvalidSetting_ShouldThrow(int maxMessages, int maxWaitMilliseconds)
    {
        // Arrange
        var loader = SystemBuilder.Create("catalog").AddLoader("loader").Subscribes(s_topic);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            loader.InBatches(maxMessages, TimeSpan.FromMilliseconds(maxWaitMilliseconds)));
    }

    [Fact]
    public void InBatches_WithAFractionalMillisecondWait_ShouldThrow()
    {
        // Arrange
        var loader = SystemBuilder.Create("catalog").AddLoader("loader").Subscribes(s_topic);

        // Act & Assert — the sidecar's bulk wait is a whole number of milliseconds.
        Assert.Throws<ArgumentOutOfRangeException>(() => loader.InBatches(10, TimeSpan.FromTicks(15_000)));
    }

    [Fact]
    public void InBatches_DeclaredTwice_ShouldThrow()
    {
        // Arrange
        var loader = SystemBuilder.Create("catalog").AddLoader("loader").Subscribes(s_topic)
            .InBatches(10, TimeSpan.FromSeconds(1));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => loader.InBatches(20, TimeSpan.FromSeconds(1)));
    }
}
