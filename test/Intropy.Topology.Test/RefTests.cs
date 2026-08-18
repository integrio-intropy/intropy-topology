namespace Intropy.Topology.Test.Refs;

public class PortRefTests
{
    [Fact]
    public void Define_WithValidName_ShouldExposeProperties()
    {
        // Act
        var port = PortRef.Define("pim");

        // Assert
        Assert.Equal("pim", port.Name);
    }

    [Theory]
    [InlineData("Pim")]
    [InlineData("order.pim")]
    [InlineData("-pim")]
    [InlineData("")]
    public void Define_WithInvalidLabel_ShouldThrowArgumentException(string name)
    {
        // Act & Assert: Define carries [ConstantExpected] (CA1857 fires on this non-constant
        // argument, proving the attribute works); suppressed here to test the runtime guard.
#pragma warning disable CA1857
        Assert.Throws<ArgumentException>(() => PortRef.Define(name));
#pragma warning restore CA1857
    }

    [Fact]
    public void Equals_WithSameName_ShouldBeEqual()
    {
        // Arrange
        var first = PortRef.Define("pim");
        var second = PortRef.Define("pim");

        // Assert
        Assert.Equal(first, second);
    }
}

public class TopicRefTests
{
    [Fact]
    public void Define_WithValidNames_ShouldExposeProperties()
    {
        // Act
        var topic = TopicRef<RawEvent>.Define("test-pubsub", "raw-events");

        // Assert
        Assert.Equal("test-pubsub", topic.PubSubName);
        Assert.Equal("raw-events", topic.TopicName);
        Assert.Equal(typeof(RawEvent), topic.ContractType);
    }

    [Theory]
    [InlineData("Bad-PubSub", "topic")]
    [InlineData("pubsub", "Bad-Topic")]
    [InlineData("pubsub", "")]
    [InlineData("", "topic")]
    [InlineData("pub sub", "topic")]
    public void Define_WithInvalidName_ShouldThrowArgumentException(string pubSubName, string topicName)
    {
        // Act & Assert: [ConstantExpected] suppressed to test the runtime guard
#pragma warning disable CA1857
        Assert.Throws<ArgumentException>(() => TopicRef<RawEvent>.Define(pubSubName, topicName));
#pragma warning restore CA1857
    }

    [Fact]
    public void Define_WithNameOver253Characters_ShouldThrowArgumentException()
    {
        // Arrange
        var longName = string.Join('.', Enumerable.Repeat("abcdefghij", 26));

        // Act & Assert: [ConstantExpected] suppressed to test the runtime guard
#pragma warning disable CA1857
        Assert.Throws<ArgumentException>(() => TopicRef<RawEvent>.Define("pubsub", longName));
#pragma warning restore CA1857
    }

    [Fact]
    public void Equals_WithSameNamesAndContract_ShouldBeEqual()
    {
        // Arrange
        var first = TopicRef<RawEvent>.Define("test-pubsub", "raw-events");
        var second = TopicRef<RawEvent>.Define("test-pubsub", "raw-events");

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Equals_WithSameNamesButDifferentContract_ShouldNotBeEqual()
    {
        // Arrange
        TopicRef first = TopicRef<RawEvent>.Define("test-pubsub", "raw-events");
        TopicRef second = TopicRef<EnrichedEvent>.Define("test-pubsub", "raw-events");

        // Assert
        Assert.NotEqual(first, second);
    }
}
