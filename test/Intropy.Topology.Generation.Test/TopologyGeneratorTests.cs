using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation.Test;

public class TopologyGeneratorTests
{
    private static readonly SystemTopology s_topology = SystemDiscovery.Discover(typeof(OrderSystem).Assembly).Topology;
    private static readonly DevelopmentManifest s_development =
        DevelopmentDiscovery.Discover(typeof(OrderSystem).Assembly, s_topology, Directory.GetCurrentDirectory());

    [Fact]
    public void Generate_ShouldEmit_OnePubSubComponentPerDistinctPubSubName()
    {
        // Act
        var artifacts = TopologyGenerator.Generate(s_topology, s_development);

        // Assert
        Assert.Contains(artifacts.Files, f => f.RelativePath == "components/pubsub-a.yaml");
        Assert.Contains(artifacts.Files, f => f.RelativePath == "components/pubsub-b.yaml");
    }

    [Fact]
    public void Generate_ShouldBackPubSubWithRedis()
    {
        // Act
        var yaml = Content("components/pubsub-a.yaml");

        // Assert — 6380, not the Redis default: `dapr init` owns 6379 on the host.
        Assert.Contains("type: \"pubsub.redis\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: \"redisHost\"", yaml, StringComparison.Ordinal);
        Assert.Contains("value: \"localhost:6380\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldEmit_OneBindingComponentPerPort_ResolvedToLocalstorage()
    {
        // Act
        var webshop = Content("components/webshop.yaml");
        var erp = Content("components/erp.yaml");

        // Assert
        Assert.Contains("type: \"bindings.localstorage\"", webshop, StringComparison.Ordinal);
        Assert.Contains("type: \"bindings.localstorage\"", erp, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldScope_PubSubToItsPublishersAndSubscribers()
    {
        // Act — pubsub-a is published by order-extractor and subscribed by order-loader.
        var yaml = Content("components/pubsub-a.yaml");

        // Assert
        Assert.Contains("scopes:", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-extractor\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-loader\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("fulfillment-extractor", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("fulfillment-loader", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithFileResolution_ShouldEmitRootPathRelativeToSystemHost()
    {
        // Act — the development resolution is relative to the SystemHost directory, so the
        // generated binding works regardless of where the artifacts land.
        var yaml = Content("components/webshop.yaml");

        // Assert
        Assert.Contains("- name: \"rootPath\"", yaml, StringComparison.Ordinal);
        Assert.Contains($"value: \"{Path.Combine("test", "webshop")}\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithUnresolvedPort_ShouldRejectAtTheManifestBoundary()
    {
        // Arrange — a port used by the topology but absent from the development manifest.
        // The manifest is the single validation boundary; the generator trusts it.
        var topic = TopicRef<string>.Define("orders", "created");
        var port = PortRef.Define("erp");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").From(port).Publishes(topic);
        builder.AddLoader("loader").Subscribes(topic);
        var development = new DevelopmentBuilder(builder.Build(), Directory.GetCurrentDirectory());

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => development.Build());
        Assert.Contains("erp", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldScope_BindingToItsUsers()
    {
        // Act — webshop is used only by order-loader.
        var yaml = Content("components/webshop.yaml");

        // Assert
        Assert.Contains("scopes:", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-loader\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("order-extractor", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldEmit_InternalPubSubPerTransactionalIntegration_ScopedToItself()
    {
        // Act — the internal hop is minted in the model, not declared as a topic edge, so it
        // does not emerge from the topic-pubsub loop; generation emits it explicitly.
        var yaml = Content("components/internal-price-sync.yaml");

        // Assert
        Assert.Contains("type: \"pubsub.redis\"", yaml, StringComparison.Ordinal);
        Assert.Contains("value: \"localhost:6380\"", yaml, StringComparison.Ordinal);
        Assert.Contains("scopes:", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"price-sync\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("order-extractor", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldCarry_InternalQueueInTheRuntimeConfig()
    {
        // Act
        var json = Content("config/price-sync.intropy.json");

        // Assert
        Assert.Contains("\"InternalQueue\"", json, StringComparison.Ordinal);
        Assert.Contains("\"PubSubName\": \"internal-price-sync\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TopicName\": \"hop\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldOmit_InternalQueueForNonTransactionalComponents()
    {
        // Act
        var json = Content("config/order-extractor.intropy.json");

        // Assert
        Assert.DoesNotContain("InternalQueue", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldEmit_PerComponentRuntimeConfig()
    {
        // Act
        var json = Content("config/order-extractor.intropy.json");

        // Assert
        Assert.Contains("\"System\": \"order-fulfillment\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Component\": \"order-extractor\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Kind\": \"Extractor\"", json, StringComparison.Ordinal);
        Assert.Contains("\"erp\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Direction\": \"In\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldEmitRuntimeConfigWithoutActivationAttributes()
    {
        // Act — activation (cron) lives in the component scaffold, not in the emitted config.
        var json = Content("config/order-loader.intropy.json");

        // Assert
        Assert.DoesNotContain("Schedule", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithOpenApiMock_ShouldEmitScopedHttpEndpoint()
    {
        // Arrange
        var topic = TopicRef<string>.Define("orders", "created");
        var service = ServiceRef.Define("idempotency-service");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").Publishes(topic).Uses(service);
        builder.AddLoader("loader").Subscribes(topic).Uses(service);
        var manifest = new DevelopmentManifest(
            [new OpenApiMock("idempotency-service", "/tmp/idempotency.yaml", "Idempotency Service", "1.0/rc")],
            []);

        // Act
        var yaml = TopologyGenerator.Generate(builder.Build(), manifest).Files
            .Single(file => file.RelativePath == "components/idempotency-service.yaml").Content;

        // Assert
        Assert.Contains("kind: HTTPEndpoint", yaml, StringComparison.Ordinal);
        Assert.Contains("baseUrl: \"http://localhost:8585/rest/Idempotency%20Service/1.0%2Frc\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"extractor\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"loader\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldBeDeterministic()
    {
        // Act
        var first = TopologyGenerator.Generate(s_topology, s_development);
        var second = TopologyGenerator.Generate(s_topology, s_development);

        // Assert
        Assert.Equal(
            first.Files.Select(f => (f.RelativePath, f.Content)),
            second.Files.Select(f => (f.RelativePath, f.Content)));
    }

    [Fact]
    public void Generate_WithStandalonePort_ShouldEmitLocalstorageBinding()
    {
        // Arrange — local generation resolves every port to localstorage; the deployed
        // binding type is deployment-owned and never declared in the topology.
        var topic = TopicRef<string>.Define("orders", "created");
        var port = PortRef.Define("placeholder");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").From(port).Publishes(topic);
        builder.AddLoader("loader").Subscribes(topic);
        var topology = builder.Build();
        var manifest = new DevelopmentManifest(
            [],
            [new PortFileResolution("placeholder", "./test/placeholder")]);

        // Act
        var artifacts = TopologyGenerator.Generate(topology, manifest);

        // Assert
        var yaml = artifacts.Files.Single(f => f.RelativePath == "components/placeholder.yaml").Content;
        Assert.Contains("type: \"bindings.localstorage\"", yaml, StringComparison.Ordinal);
    }

    private static string Content(string relativePath)
    {
        var artifacts = TopologyGenerator.Generate(s_topology, s_development);
        return artifacts.Files.Single(f => f.RelativePath == relativePath).Content;
    }
}
