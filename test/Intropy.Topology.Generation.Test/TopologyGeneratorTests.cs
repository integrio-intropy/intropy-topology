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
        var artifacts = TopologyGenerator.Generate(s_topology, s_development, Directory.GetCurrentDirectory());

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
    public void Generate_WithFileResolution_ShouldEmitRootPathAbsoluteUnderSystemHost()
    {
        // Act — the development resolution is declared relative to the SystemHost directory,
        // but the binding metadata must be absolute: the artifacts land in per-run temp dirs
        // and the sidecar's cwd is not guaranteed.
        var yaml = Content("components/webshop.yaml");

        // Assert
        Assert.Contains("- name: \"rootPath\"", yaml, StringComparison.Ordinal);
        var expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "test", "webshop"));
        Assert.Contains($"value: \"{expected}\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithUnresolvedPort_ShouldRejectAtTheManifestBoundary()
    {
        // Arrange — a port used by the topology but absent from the development manifest.
        // The manifest is the single validation boundary; the generator trusts it.
        var message = MessageRef<string>.Define("created", pubSub: "orders");
        var port = PortRef.Define("erp");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").From(port).Publishes(message);
        builder.AddLoader("loader").Subscribes(message);
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
    public void Generate_ShouldEmit_TheSystemsPubSub_ScopedToTheTransactionalIntegrationItCarries()
    {
        // Act — the internal queue is minted in the model, not declared as a topic edge, so its
        // pubsub does not emerge from the topics; generation emits it and scopes it explicitly.
        var yaml = Content("components/order-fulfillment-pubsub.yaml");

        // Assert
        Assert.Contains("type: \"pubsub.redis\"", yaml, StringComparison.Ordinal);
        Assert.Contains("value: \"localhost:6380\"", yaml, StringComparison.Ordinal);
        Assert.Contains("scopes:", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"price-sync\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("order-extractor", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithOnlyTransactionalIntegrations_ShouldEmitOneSystemPubSubScopedToAllOfThem()
    {
        // Arrange — no messages, so no topics: the system's pubsub exists only for the queues.
        var builder = SystemBuilder.Create("tx-flow");
        builder.AddTransactionalIntegration("order-sync").From(PortRef.Define("order-in")).To(PortRef.Define("order-out"));
        builder.AddTransactionalIntegration("price-sync").From(PortRef.Define("price-in")).To(PortRef.Define("price-out"));
        builder.AddTransactionalIntegration("stock-sync").From(PortRef.Define("stock-in")).To(PortRef.Define("stock-out"));
        PortFileResolution[] resolutions =
        [
            new("order-in", "./test/order-in"), new("order-out", "./test/order-out"),
            new("price-in", "./test/price-in"), new("price-out", "./test/price-out"),
            new("stock-in", "./test/stock-in"), new("stock-out", "./test/stock-out"),
        ];

        // Act
        var files = TopologyGenerator.Generate(builder.Build(), new DevelopmentManifest([], resolutions, []),
            Directory.GetCurrentDirectory()).Files;

        // Assert
        var pubSub = Assert.Single(files, f => f.Content.Contains("type: \"pubsub.redis\"", StringComparison.Ordinal));
        Assert.Equal("components/tx-flow-pubsub.yaml", pubSub.RelativePath);
        Assert.Contains("- \"order-sync\"", pubSub.Content, StringComparison.Ordinal);
        Assert.Contains("- \"price-sync\"", pubSub.Content, StringComparison.Ordinal);
        Assert.Contains("- \"stock-sync\"", pubSub.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldCarry_InternalQueueInTheRuntimeConfig()
    {
        // Act
        var json = Content("config/price-sync.intropy.json");

        // Assert
        Assert.Contains("\"InternalQueue\"", json, StringComparison.Ordinal);
        Assert.Contains("\"PubSubName\": \"order-fulfillment-pubsub\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TopicName\": \"internal-price-sync\"", json, StringComparison.Ordinal);
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
        var message = MessageRef<string>.Define("created", pubSub: "orders");
        var service = ServiceRef.Define("idempotency-service");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").Publishes(message).Calls(service);
        builder.AddLoader("loader").Subscribes(message).Calls(service);
        var manifest = new DevelopmentManifest(
            [new OpenApiMock("idempotency-service", "/tmp/idempotency.yaml", "Idempotency Service", "1.0/rc")],
            [],
            []);

        // Act
        var yaml = TopologyGenerator.Generate(builder.Build(), manifest, Directory.GetCurrentDirectory()).Files
            .Single(file => file.RelativePath == "components/idempotency-service.yaml").Content;

        // Assert
        Assert.Contains("kind: HTTPEndpoint", yaml, StringComparison.Ordinal);
        Assert.Contains("baseUrl: \"http://localhost:8585/rest/Idempotency%20Service/1.0%2Frc\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"extractor\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"loader\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenApiMock_AppId_ShouldStillReadTheServiceAppId()
    {
        // Arrange — AppId predates port mocks; callers that read it keep compiling and reading
        // the service's app ID.
        var mock = new OpenApiMock("idempotency-service", "/tmp/idempotency.yaml", "Idempotency Service", "1.0.0");

        // Act
#pragma warning disable CS0618 // The obsolete alias is exactly what is under test.
        var appId = mock.AppId;
#pragma warning restore CS0618

        // Assert
        Assert.Equal("idempotency-service", appId);
        Assert.Equal(MockTarget.Service, mock.Target);
    }

    [Fact]
    public void OpenApiMock_FourValueDeconstruction_ShouldStillBind()
    {
        // Arrange
        var mock = new OpenApiMock("invoice-api", "/tmp/invoice-api.yaml", "Invoice API", "1.0.0", MockTarget.Port);

        // Act
        var (name, artifact, title, version) = mock;
        var (_, _, _, _, target) = mock;

        // Assert
        Assert.Equal(("invoice-api", "/tmp/invoice-api.yaml", "Invoice API", "1.0.0"), (name, artifact, title, version));
        Assert.Equal(MockTarget.Port, target);
    }

    [Fact]
    public void Generate_WithPortMock_ShouldEmitAnHttpBindingNamedAfterThePort()
    {
        // Arrange
        var message = MessageRef<string>.Define("created", pubSub: "orders");
        var port = PortRef.Define("invoice-api");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").Publishes(message);
        builder.AddLoader("loader").Subscribes(message).To(port);
        var manifest = new DevelopmentManifest(
            [new OpenApiMock("invoice-api", "/tmp/invoice-api.yaml", "Invoice API", "1.0.0", MockTarget.Port)],
            [],
            []);

        // Act
        var files = TopologyGenerator.Generate(builder.Build(), manifest, Directory.GetCurrentDirectory()).Files;
        var yaml = files.Single(file => file.RelativePath == "components/invoice-api.yaml").Content;

        // Assert: the port keeps its binding name, so component code is unchanged; no
        // HTTPEndpoint is emitted for a port.
        Assert.Contains("kind: Component", yaml, StringComparison.Ordinal);
        Assert.Contains("name: \"invoice-api\"", yaml, StringComparison.Ordinal);
        Assert.Contains("type: \"bindings.http\"", yaml, StringComparison.Ordinal);
        Assert.Contains("value: \"http://localhost:8585/rest/Invoice%20API/1.0.0\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"loader\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain(files, file => file.Content.Contains("kind: HTTPEndpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_WithABatchingLoader_ShouldEmitItsBulkSettingsInTheRuntimeConfig()
    {
        // Arrange
        var topic = MessageRef<string>.Define("product-changed", pubSub: "catalog");
        var builder = SystemBuilder.Create("catalog");
        builder.AddExtractor("extractor").Publishes(topic);
        builder.AddLoader("loader").Subscribes(topic, configure: sub => sub.InBatches(200, TimeSpan.FromSeconds(2)));
        var manifest = new DevelopmentManifest([], [], []);

        // Act
        var json = TopologyGenerator.Generate(builder.Build(), manifest, Directory.GetCurrentDirectory()).Files
            .Single(file => file.RelativePath == "config/loader.intropy.json").Content;

        // Assert
        Assert.Contains("\"MaxMessages\": 200", json, StringComparison.Ordinal);
        Assert.Contains("\"MaxWaitMilliseconds\": 2000", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithALoaderReceivingOneMessageAtATime_ShouldEmitNoBulkSettings()
    {
        // Act
        var json = Content("config/order-loader.intropy.json");

        // Assert
        Assert.DoesNotContain("Bulk", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldBeDeterministic()
    {
        // Act
        var first = TopologyGenerator.Generate(s_topology, s_development, Directory.GetCurrentDirectory());
        var second = TopologyGenerator.Generate(s_topology, s_development, Directory.GetCurrentDirectory());

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
        var message = MessageRef<string>.Define("created", pubSub: "orders");
        var port = PortRef.Define("placeholder");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").From(port).Publishes(message);
        builder.AddLoader("loader").Subscribes(message);
        var topology = builder.Build();
        var manifest = new DevelopmentManifest(
            [],
            [new PortFileResolution("placeholder", "./test/placeholder")],
            []);

        // Act
        var artifacts = TopologyGenerator.Generate(topology, manifest, Directory.GetCurrentDirectory());

        // Assert
        var yaml = artifacts.Files.Single(f => f.RelativePath == "components/placeholder.yaml").Content;
        Assert.Contains("type: \"bindings.localstorage\"", yaml, StringComparison.Ordinal);
    }

    private static string Content(string relativePath)
    {
        var artifacts = TopologyGenerator.Generate(s_topology, s_development, Directory.GetCurrentDirectory());
        return artifacts.Files.Single(f => f.RelativePath == relativePath).Content;
    }
}
