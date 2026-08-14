using Intropy.Topology.Model;

namespace Intropy.Topology.Aspire.Test;

public class StartupOrderingTests
{
    [Fact]
    public void SubscriberPrecedence_WithPublisherSubscriberPair_ShouldYieldOneEdge()
    {
        // Arrange
        var topology = Topology(
            ("order-raw", Publishers: ["extractor"], Subscribers: ["loader"]));

        // Act
        var precedence = StartupOrdering.SubscriberPrecedence(topology);

        // Assert
        Assert.Equal(["loader"], precedence["extractor"]);
    }

    [Fact]
    public void SubscriberPrecedence_WithSelfLoop_ShouldYieldNoEdge()
    {
        // Arrange: a component consuming its own topic is not an orderable edge
        var topology = Topology(
            ("events", Publishers: ["processor"], Subscribers: ["processor"]));

        // Act
        var precedence = StartupOrdering.SubscriberPrecedence(topology);

        // Assert
        Assert.Empty(precedence);
    }

    [Fact]
    public void SubscriberPrecedence_WithPublisherOnly_ShouldYieldNoEntry()
    {
        // Arrange
        var topology = Topology(
            ("order-raw", Publishers: ["extractor"], Subscribers: []));

        // Act
        var precedence = StartupOrdering.SubscriberPrecedence(topology);

        // Assert
        Assert.Empty(precedence);
    }

    [Fact]
    public void HasCycle_WithAcyclicGraph_ShouldReportFalse()
    {
        // Arrange: extractor -> loader -> sink
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["extractor"] = ["loader"],
            ["loader"] = ["sink"],
        };

        // Act & Assert
        Assert.False(StartupOrdering.HasCycle(edges));
    }

    [Fact]
    public void HasCycle_WithCycle_ShouldReportTrue()
    {
        // Arrange: a -> b -> a
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["a"] = ["b"],
            ["b"] = ["a"],
        };

        // Act & Assert
        Assert.True(StartupOrdering.HasCycle(edges));
    }

    private static SystemTopology Topology(
        params (string Topic, string[] Publishers, string[] Subscribers)[] topics) =>
        new()
        {
            SystemName = "test-system",
            Components = [],
            Topics =
            [
                .. topics.Select(t => new TopicResource
                {
                    PubSubName = "pubsub",
                    TopicName = t.Topic,
                    ContractTypeName = "Test.Event",
                    Publishers = t.Publishers,
                    Subscribers = t.Subscribers,
                }),
            ],
            Ports = [],
            Services = [],
        };
}
