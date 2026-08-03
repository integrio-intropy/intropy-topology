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
    public void Generate_ShouldEmit_OneBindingComponentPerConnector_TypedFromTransport()
    {
        // Act
        var webshop = Content("components/binding.webshop.yaml");
        var erp = Content("components/binding.erp.yaml");

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
    public void Generate_WithFileResolution_ShouldAbsolutizeRootPath()
    {
        // Act — artifacts land in per-run temp dirs, so the development resolution is absolute.
        var yaml = Content("components/binding.webshop.yaml");

        // Assert
        Assert.Contains("- name: \"rootPath\"", yaml, StringComparison.Ordinal);
        Assert.Contains($"value: \"{Path.GetFullPath("./test/webshop")}\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithUnresolvedConnector_ShouldThrow()
    {
        // Arrange — a connector used by the topology but absent from the development manifest
        var topic = TopicRef<string>.Define("orders", "created");
        var connector = ConnectorRef.Define("erp", Transport.Sftp());
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").From(connector).Publishes(topic);
        builder.AddLoader("loader").Subscribes(topic);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            TopologyGenerator.Generate(builder.Build(), new DevelopmentManifest([], [])));
        Assert.Contains("erp", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldScope_BindingToItsUsers()
    {
        // Act — webshop is used only by order-loader.
        var yaml = Content("components/binding.webshop.yaml");

        // Assert
        Assert.Contains("scopes:", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-loader\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("order-extractor", yaml, StringComparison.Ordinal);
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
        Assert.Contains("\"binding.erp\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Direction\": \"In\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithSchedule_ShouldEmitScheduleInRuntimeConfig()
    {
        // Act
        var json = Content("config/order-extractor.intropy.json");

        // Assert
        Assert.Contains("\"Schedule\": \"*/5 * * * *\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithoutSchedule_ShouldOmitScheduleFromRuntimeConfig()
    {
        // Act — unscheduled components' config stays byte-identical to the pre-schedule shape.
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
    public void Generate_WithDefaultTransportConnector_ShouldThrow()
    {
        // Arrange — a connector using the placeholder default transport should not reach generation
        var topic = TopicRef<string>.Define("orders", "created");
#pragma warning disable CS0618
        var connector = ConnectorRef.Define("placeholder", Transport.Default());
#pragma warning restore CS0618
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").From(connector).Publishes(topic);
        builder.AddLoader("loader").Subscribes(topic);

        // Act & Assert — TopologyRules should reject Default, but Generate guards as belt-and-suspenders
        // The builder's Build() runs validation, so the default transport rule catches it first.
        var exception = Assert.Throws<TopologyValidationException>(() => builder.Build());
        Assert.Contains("placeholder default transport", exception.Message, StringComparison.Ordinal);
    }

    private static string Content(string relativePath)
    {
        var artifacts = TopologyGenerator.Generate(s_topology, s_development);
        return artifacts.Files.Single(f => f.RelativePath == relativePath).Content;
    }
}
