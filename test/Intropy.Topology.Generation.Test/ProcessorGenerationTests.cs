using System.Text.Json;
using Intropy.Topology;

namespace Intropy.Topology.Generation.Test;

public sealed record LineAdded;

public sealed record TotalChanged;

/// <summary>
/// A processor runs on two state components the topology names after it: the plain one and the
/// one whose transactional outbox publishes to the processor's channel. Locally both are PostgreSQL;
/// the graph carries them for deployment, which chooses its own store.
/// </summary>
public class ProcessorGenerationTests
{
    private static readonly MessageRef<LineAdded> s_lines =
        MessageRef<LineAdded>.Define("order-line-added", topic: "order-lines");

    private static readonly MessageRef<TotalChanged> s_totals =
        MessageRef<TotalChanged>.Define("order-total-changed", topic: "order-totals");

    private static readonly DevelopmentManifest s_noDevelopment = new([], [], []);

    private static SystemBuilder Orders()
    {
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("line-extractor").Publishes(s_lines);
        builder.AddProcessor("order-totals").Subscribes(s_lines).Publishes(s_totals);
        builder.AddLoader("total-loader").Subscribes(s_totals);
        return builder;
    }

    private static string Generate(string relativePath) =>
        TopologyGenerator.Generate(Orders().Build(), s_noDevelopment, Directory.GetCurrentDirectory()).Files
            .Single(f => f.RelativePath == relativePath).Content;

    [Fact]
    public void Generate_ForAProcessor_ShouldEmitAPlainRedisStateScopedToIt()
    {
        // Act
        var yaml = Generate("components/order-totals-state.yaml");

        // Assert
        Assert.Contains("name: \"order-totals-state\"", yaml, StringComparison.Ordinal);
        Assert.Contains("type: \"state.postgresql\"\n  version: v2", yaml, StringComparison.Ordinal);
        Assert.Contains("value: \"host=localhost port=6432 user=postgres database=postgres sslmode=disable\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("outbox", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-totals\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("total-loader", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ForAProcessor_ShouldEmitAnOutboxStatePublishingToItsChannel()
    {
        // Act
        var yaml = Generate("components/order-totals-state-outbox.yaml");

        // Assert
        Assert.Contains("type: \"state.postgresql\"\n  version: v2", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: \"outboxPublishPubsub\"\n    value: \"orders-pubsub\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: \"outboxPublishTopic\"\n    value: \"order-totals\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-totals\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ForAProcessor_ShouldEmitItsSubscriptionAndPubSubScope()
    {
        // Act
        var subscription = Generate("components/order-totals-subscription.yaml");
        var pubSub = Generate("components/orders-pubsub.yaml");

        // Assert — it receives like a loader, and its sidecar loads the pubsub the outbox publishes on.
        Assert.Contains("match: \"event.type == 'order-line-added'\"", subscription, StringComparison.Ordinal);
        Assert.Contains("- \"order-totals\"", pubSub, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ForAProcessor_ShouldDescribeItsStateInTheComponentConfig()
    {
        // Act
        using var json = JsonDocument.Parse(Generate("config/order-totals.intropy.json"));
        var intropy = json.RootElement.GetProperty("Intropy");

        // Assert
        Assert.Equal("Processor", intropy.GetProperty("Kind").GetString());
        var state = intropy.GetProperty("State");
        Assert.Equal("order-totals-state", state.GetProperty("StateStoreName").GetString());
        Assert.Equal("order-totals-state-outbox", state.GetProperty("OutboxStateStoreName").GetString());
        Assert.Equal("order-totals", state.GetProperty("Outbox").GetProperty("TopicName").GetString());
    }

    [Fact]
    public void Generate_ForOtherKinds_ShouldEmitNoStateComponents()
    {
        // Act
        var files = TopologyGenerator.Generate(Orders().Build(), s_noDevelopment, Directory.GetCurrentDirectory()).Files;

        // Assert
        Assert.Equal(2, files.Count(f => f.RelativePath.Contains("-state", StringComparison.Ordinal)));
        using var loaderConfig = JsonDocument.Parse(files.Single(f => f.RelativePath == "config/total-loader.intropy.json").Content);
        Assert.False(loaderConfig.RootElement.GetProperty("Intropy").TryGetProperty("State", out _));
    }

    [Fact]
    public void GraphJson_ShouldEmitAProcessorsKindAndState()
    {
        // Act
        using var json = JsonDocument.Parse(IntropyGenerate.GraphJson(Orders().Build(), development: null));
        var components = json.RootElement.GetProperty("components").EnumerateArray().ToArray();
        var processor = components.Single(c => c.GetProperty("name").GetString() == "order-totals");
        var loader = components.Single(c => c.GetProperty("name").GetString() == "total-loader");

        // Assert
        Assert.Equal("processor", processor.GetProperty("kind").GetString());
        var state = processor.GetProperty("state");
        Assert.Equal("order-totals-state", state.GetProperty("stateStore").GetString());
        Assert.Equal("order-totals-state-outbox", state.GetProperty("outboxStateStore").GetString());
        Assert.Equal("orders-pubsub", state.GetProperty("outbox").GetProperty("pubsub").GetString());
        Assert.Equal("order-totals", state.GetProperty("outbox").GetProperty("topic").GetString());
        // Two topics, so no single one is its channel; one pubsub is.
        Assert.False(processor.GetProperty("channel").TryGetProperty("topic", out _));
        Assert.False(loader.TryGetProperty("state", out _));
    }
}
