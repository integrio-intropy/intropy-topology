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

public class MessageRefTests
{
    [Fact]
    public void Define_WithTwoArguments_ShouldDefaultTopicToName()
    {
        // Act
        var message = MessageRef<RawEvent>.Define("raw-events", "test-pubsub");

        // Assert
        Assert.Equal("raw-events", message.Name);
        Assert.Equal("test-pubsub", message.PubSubName);
        Assert.Equal("raw-events", message.TopicName);
        Assert.Equal(typeof(RawEvent), message.ContractType);
    }

    [Fact]
    public void Define_WithThreeArguments_ShouldExposeProperties()
    {
        // Act
        var message = MessageRef<RawEvent>.Define("raw-events", "test-pubsub", "raw-events-topic");

        // Assert
        Assert.Equal("raw-events", message.Name);
        Assert.Equal("test-pubsub", message.PubSubName);
        Assert.Equal("raw-events-topic", message.TopicName);
        Assert.Equal(typeof(RawEvent), message.ContractType);
    }

    [Theory]
    [InlineData("Bad-Name", "pubsub", "topic")]
    [InlineData("name", "Bad-PubSub", "topic")]
    [InlineData("name", "pubsub", "Bad-Topic")]
    [InlineData("", "pubsub", "topic")]
    [InlineData("name", "", "topic")]
    [InlineData("name", "pubsub", "")]
    [InlineData("pub sub", "pubsub", "topic")]
    public void Define_WithInvalidName_ShouldThrowArgumentException(string name, string pubSubName, string topicName)
    {
        // Act & Assert: [ConstantExpected] suppressed to test the runtime guard
#pragma warning disable CA1857
        Assert.Throws<ArgumentException>(() => MessageRef<RawEvent>.Define(name, pubSubName, topicName));
#pragma warning restore CA1857
    }

    [Fact]
    public void Define_WithNameOver253Characters_ShouldThrowArgumentException()
    {
        // Arrange
        var longName = string.Join('.', Enumerable.Repeat("abcdefghij", 26));

        // Act & Assert: [ConstantExpected] suppressed to test the runtime guard
#pragma warning disable CA1857
        Assert.Throws<ArgumentException>(() => MessageRef<RawEvent>.Define(longName, "pubsub"));
#pragma warning restore CA1857
    }

    [Fact]
    public void Equals_WithSameNamesAndContract_ShouldBeEqual()
    {
        // Arrange
        var first = MessageRef<RawEvent>.Define("raw-events", "test-pubsub");
        var second = MessageRef<RawEvent>.Define("raw-events", "test-pubsub");

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Equals_WithSameNamesButDifferentContract_ShouldNotBeEqual()
    {
        // Arrange
        MessageRef first = MessageRef<RawEvent>.Define("raw-events", "test-pubsub");
        MessageRef second = MessageRef<EnrichedEvent>.Define("raw-events", "test-pubsub");

        // Assert
        Assert.NotEqual(first, second);
    }
}
