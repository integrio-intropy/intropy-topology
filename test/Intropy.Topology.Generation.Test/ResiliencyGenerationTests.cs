namespace Intropy.Topology.Generation.Test;

/// <summary>
/// Every pubsub gets an inbound retry policy, scoped like the pubsub: without one the sidecar drops a
/// delivery the component answers with RETRY instead of redelivering it.
/// </summary>
public class ResiliencyGenerationTests
{
    private static readonly MessageRef<LineAdded> s_lines =
        MessageRef<LineAdded>.Define("order-line-added", topic: "order-lines");

    [Fact]
    public void Generate_ForAPubSub_ShouldEmitAnInboundRetryPolicyScopedLikeIt()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("line-extractor").Publishes(s_lines);
        builder.AddLoader("line-loader").Subscribes(s_lines);

        // Act
        var yaml = TopologyGenerator.Generate(builder.Build(), new DevelopmentManifest([], [], []), Directory.GetCurrentDirectory())
            .Files.Single(f => f.RelativePath == "components/orders-pubsub-resiliency.yaml").Content;

        // Assert
        Assert.Contains("kind: Resiliency", yaml, StringComparison.Ordinal);
        Assert.Contains("name: \"orders-pubsub-resiliency\"", yaml, StringComparison.Ordinal);
        Assert.Contains("policy: exponential", yaml, StringComparison.Ordinal);
        Assert.Contains("      \"orders-pubsub\":\n        inbound:\n          retry: redeliver", yaml, StringComparison.Ordinal);
        Assert.Contains("scopes:\n- \"line-extractor\"\n- \"line-loader\"", yaml, StringComparison.Ordinal);
    }
}
