using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation.Test;

public sealed record OrderPlaced;

public sealed record OrderCancelled;

/// <summary>
/// Every subscribing component gets a declarative Dapr <c>Subscription</c>: one routing rule per
/// handled message (its name is its CloudEvent type) and a default route for the channel's other
/// messages.
/// </summary>
public class SubscriptionGenerationTests
{
    private static readonly MessageRef<OrderPlaced> s_placed =
        MessageRef<OrderPlaced>.Define("fluxia.orders.order-placed", topic: "orders");

    private static readonly MessageRef<OrderCancelled> s_cancelled =
        MessageRef<OrderCancelled>.Define("fluxia.orders.order-cancelled", topic: "orders");

    private static readonly DevelopmentManifest s_noDevelopment = new([], [], []);

    private static string Generate(SystemBuilder builder, string relativePath) =>
        TopologyGenerator.Generate(builder.Build(), s_noDevelopment, Directory.GetCurrentDirectory()).Files
            .Single(f => f.RelativePath == relativePath).Content;

    private static SystemBuilder Orders(Action<Intropy.Topology.Building.SubscriptionBuilder>? subscription = null)
    {
        // One extractor per published message; the channel carries two messages with
        // their own contracts.
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("order-extractor").Publishes(s_placed);
        builder.AddExtractor("cancellation-extractor").Publishes(s_cancelled);
        builder.AddLoader("fulfillment").Subscribes(s_placed, configure: subscription);
        return builder;
    }

    [Fact]
    public void Generate_ForALoader_ShouldEmitASubscriptionWithARulePerHandledMessage()
    {
        // Act
        var yaml = Generate(Orders(sub => sub.AlsoHandles(s_cancelled)),
            "components/fulfillment-subscription.yaml");

        // Assert
        Assert.Equal(
            """
            apiVersion: dapr.io/v2alpha1
            kind: Subscription
            metadata:
              name: "fulfillment-subscription"
            spec:
              pubsubname: "pubsub"
              topic: "orders"
              routes:
                rules:
                - match: "event.type == 'fluxia.orders.order-placed'"
                  path: "/fluxia.orders.order-placed"
                - match: "event.type == 'fluxia.orders.order-cancelled'"
                  path: "/fluxia.orders.order-cancelled"
                default: "/unhandled"
            scopes:
            - "fulfillment"

            """, yaml);
    }

    [Fact]
    public void Generate_ForAHandledMessageWithAContentFilter_ShouldAddTheFilterToItsRule()
    {
        // Act
        var yaml = Generate(Orders(sub => sub
                .AlsoHandles(s_cancelled, when: "event.data.reason == 'customer-request'")
                .IgnoreOthers()),
            "components/fulfillment-subscription.yaml");

        // Assert
        Assert.Contains("""
                rules:
                - match: "event.type == 'fluxia.orders.order-placed'"
                  path: "/fluxia.orders.order-placed"
                - match: "event.type == 'fluxia.orders.order-cancelled' && (event.data.reason == 'customer-request')"
                  path: "/fluxia.orders.order-cancelled"
                default: "/unhandled"
            """, yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ForABatchingLoader_ShouldEmitTheBulkSettingsOnTheSubscription()
    {
        // Act
        var yaml = Generate(Orders(sub => sub.InBatches(100, TimeSpan.FromMilliseconds(500))),
            "components/fulfillment-subscription.yaml");

        // Assert
        Assert.Contains("""
              bulkSubscribe:
                enabled: true
                maxMessagesCount: 100
                maxAwaitDurationMs: 500
            """, yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ForATransactionalIntegration_ShouldEmitASubscriptionToItsInternalHop()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddTransactionalIntegration("order-sync").From(PortRef.Define("webshop")).To(PortRef.Define("erp"));
        var development = new DevelopmentManifest([],
            [new PortFileResolution("webshop", "./test/webshop"), new PortFileResolution("erp", "./test/erp")], []);

        // Act
        var yaml = TopologyGenerator.Generate(builder.Build(), development, Directory.GetCurrentDirectory()).Files
            .Single(f => f.RelativePath == "components/order-sync-subscription.yaml").Content;

        // Assert — one kind of message on the hop: the default route only
        Assert.Contains("pubsubname: \"internal-order-sync\"", yaml, StringComparison.Ordinal);
        Assert.Contains("topic: \"hop\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("rules:", yaml, StringComparison.Ordinal);
        Assert.Contains("default: \"/unhandled\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-sync\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ForAnExtractor_ShouldEmitNoSubscription()
    {
        // Act
        var files = TopologyGenerator.Generate(Orders().Build(), s_noDevelopment,
            Directory.GetCurrentDirectory()).Files;

        // Assert
        Assert.DoesNotContain(files, f => f.RelativePath == "components/order-extractor-subscription.yaml");
    }

    [Fact]
    public void Generate_ShouldWriteTheHandledMessagesAndTheirPolicyIntoTheRuntimeConfig()
    {
        // Act
        var json = Generate(Orders(sub => sub.IgnoreOthers()), "config/fulfillment.intropy.json");

        // Assert
        Assert.Contains("\"fluxia.orders.order-placed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Unhandled\": \"Ignore\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldWriteEachPublishedMessageIntoThePublishersRuntimeConfig()
    {
        // Act
        var orderJson = Generate(Orders(), "config/order-extractor.intropy.json");
        var cancellationJson = Generate(Orders(), "config/cancellation-extractor.intropy.json");

        // Assert
        Assert.Contains("\"Message\": \"fluxia.orders.order-placed\"", orderJson, StringComparison.Ordinal);
        Assert.DoesNotContain("fluxia.orders.order-cancelled", orderJson, StringComparison.Ordinal);
        Assert.Contains("\"Message\": \"fluxia.orders.order-cancelled\"", cancellationJson, StringComparison.Ordinal);
        Assert.DoesNotContain("fluxia.orders.order-placed", cancellationJson, StringComparison.Ordinal);
    }
}
