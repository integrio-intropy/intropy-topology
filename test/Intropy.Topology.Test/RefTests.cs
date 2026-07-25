namespace Intropy.Topology.Test.Refs;

public class ConnectorRefTests
{
    [Fact]
    public void Define_WithValidName_ShouldExposeProperties()
    {
        // Act
        var connector = ConnectorRef.Define("pim", Transport.File("./test/pim"));

        // Assert
        Assert.Equal("pim", connector.Name);
        Assert.Equal(Transport.File("./test/pim"), connector.Transport);
    }

    [Theory]
    [InlineData("Pim")]
    [InlineData("binding.pim")]
    [InlineData("-pim")]
    [InlineData("")]
    public void Define_WithInvalidLabel_ShouldThrowArgumentException(string name)
    {
        // Act & Assert: Define carries [ConstantExpected] (CA1857 fires on this non-constant
        // argument, proving the attribute works); suppressed here to test the runtime guard.
#pragma warning disable CA1857
        Assert.Throws<ArgumentException>(() => ConnectorRef.Define(name, Transport.File("./test/pim")));
#pragma warning restore CA1857
    }

    [Fact]
    public void Define_WithNullTransport_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ConnectorRef.Define("pim", null!));
    }

    [Fact]
    public void Equals_WithSameNameAndTransport_ShouldBeEqual()
    {
        // Arrange
        var first = ConnectorRef.Define("pim", Transport.File("./test/pim"));
        var second = ConnectorRef.Define("pim", Transport.File("./test/pim"));

        // Assert
        Assert.Equal(first, second);
    }
}

public class TransportTests
{
    [Fact]
    public void File_ShouldExposeDaprTypeAndRootPath()
    {
        // Act
        var transport = Transport.File("./test/source");

        // Assert
        var file = Assert.IsType<FileTransport>(transport);
        Assert.Equal("bindings.localstorage", file.DaprType);
        Assert.Equal("./test/source", file.RootPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void File_WithEmptyRootPath_ShouldThrowArgumentException(string rootPath)
    {
        // Act & Assert: [ConstantExpected] suppressed to test the runtime guard
#pragma warning disable CA1857
        Assert.Throws<ArgumentException>(() => Transport.File(rootPath));
#pragma warning restore CA1857
    }

    [Fact]
    public void Equals_WithSameRootPath_ShouldBeEqual()
    {
        // Assert
        Assert.Equal(Transport.File("./test/source"), Transport.File("./test/source"));
        Assert.NotEqual(Transport.File("./test/source"), Transport.File("./test/other"));
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
