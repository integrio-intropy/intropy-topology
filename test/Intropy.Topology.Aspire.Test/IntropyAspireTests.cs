using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Intropy.Topology;
using Intropy.Topology.Generation;
using Intropy.Topology.Model;

namespace Intropy.Topology.Aspire.Test;

[Collection("RedisPort")]
public sealed class IntropyAspireTests : IDisposable
{
    private sealed record RawOrder(string OrderNumber);

    private static readonly TopicRef<RawOrder> s_raw = TopicRef<RawOrder>.Define("pubsub-a", "order-raw");
    private static readonly PortRef s_webshop = PortRef.Define("webshop");
    private static readonly PortRef s_erp = PortRef.Define("erp");

    private const string GeneratedRoot = "/tmp/intropy-aspire-test";

    // Apply resolves each component to a project on disk by folder convention, so every
    // test gets a workspace with the AppHost and one flat-layout project per component.
    private readonly string _workspace = Directory.CreateTempSubdirectory("intropy-aspire-").FullName;

    private string AppHostDir => Path.Combine(_workspace, "system-host");

    public IntropyAspireTests()
    {
        Directory.CreateDirectory(AppHostDir);
        WriteProject("order-extractor");
        WriteProject("order-loader");
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private void WriteProject(string componentName)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_workspace, componentName)).FullName;
        File.WriteAllText(Path.Combine(dir, componentName + ".csproj"), "<Project />");
    }

    private IDistributedApplicationBuilder CreateBuilder() =>
        DistributedApplication.CreateBuilder(new DistributedApplicationOptions { ProjectDirectory = AppHostDir });

    private static SystemTopology Topology()
    {
        var s = SystemBuilder.Create("order-flow");
        s.AddExtractor("order-extractor").From(s_webshop).Publishes(s_raw);
        s.AddLoader("order-loader").Subscribes(s_raw).To(s_erp);
        return s.Build();
    }

    [Fact]
    public void Apply_WhenRedisPortIsOccupied_ShouldExplainHowToRecover()
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, IntropyAspire.RedisPort);
        listener.Start();
        var builder = CreateBuilder();

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => IntropyAspire.Apply(builder, Topology(), GeneratedRoot));

        // Assert
        Assert.Contains("Redis requires host port 6380", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Stop the conflicting process", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WithCyclicTopics_ShouldWarnAndSkipOrdering()
    {
        // Arrange: order-extractor and order-loader publish and subscribe each other's
        // topics. The block builders make a cycle undeclarable, so the model is built
        // directly. The cycle cannot be ordered; Apply must warn and skip ordering.
        var topology = Topology() with
        {
            Topics =
            [
                new TopicResource
                {
                    PubSubName = "pubsub-a",
                    TopicName = "order-raw",
                    ContractTypeName = "Test.RawOrder",
                    Publishers = ["order-extractor"],
                    Subscribers = ["order-loader"],
                },
                new TopicResource
                {
                    PubSubName = "pubsub-b",
                    TopicName = "order-processed",
                    ContractTypeName = "Test.RawOrder",
                    Publishers = ["order-loader"],
                    Subscribers = ["order-extractor"],
                },
            ],
        };
        var builder = CreateBuilder();
        var originalError = Console.Error;
        using var error = new StringWriter();
        Console.SetError(error);

        try
        {
            // Act
            IntropyAspire.Apply(builder, topology, GeneratedRoot);
        }
        finally
        {
            Console.SetError(originalError);
        }

        // Assert
        Assert.Contains("publish/subscribe cycle", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ComponentKind.Extractor, true)]
    [InlineData(ComponentKind.TransactionalIntegration, true)]
    [InlineData(ComponentKind.Loader, false)]
    public void IsRunToCompletion_ShouldFollowTheKindsSingleLegitimateHost(
        ComponentKind kind, bool expected)
    {
        // Extractors and transactional integrations are hosted by the framework's
        // run-to-completion runners; loaders stay resident on their subscription.
        Assert.Equal(expected, IntropyAspire.IsRunToCompletion(kind));
    }

    [Fact]
    public void RunToCompletionSidecarsFor_ShouldExemptRunToCompletionSidecarsOnly()
    {
        // Act
        var exempt = IntropyAspire.RunToCompletionSidecarsFor(Topology());

        // Assert — the extractor runs to completion; the resident loader's sidecar keeps recovery.
        Assert.Equal(["order-extractor-dapr-cli"], exempt.Names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Apply_ShouldAddRedisBackend()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot);

        // Assert
        Assert.Contains(builder.Resources, r => r.Name == "redis");
    }

    [Fact]
    public void MicrocksOrigin_ShouldAgreeWithTheGenerationCompositionPoint()
    {
        // The graph contract forces the dependency-free Generation package to know the
        // local Microcks origin too; this is the guard against the two drifting.
        Assert.Equal(IntropyAspire.MicrocksOrigin, LocalMockEndpoints.Origin);
    }

    [Fact]
    public void Apply_WithDevelopmentMocks_ShouldAddOneMicrocksResource()
    {
        // Arrange
        var builder = CreateBuilder();
        var development = new DevelopmentManifest(
            [new OpenApiMock("idempotency-service", "/tmp/idempotency.yaml", "Idempotency", "1")],
            []);

        // Act
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot, development);

        // Assert
        Assert.Contains(builder.Resources, resource => resource.Name == "microcks");
    }

    [Fact]
    public void CountMatchingServices_WithTopLevelArray_ShouldFindService()
    {
        // Arrange
        using var services = JsonDocument.Parse(
            """
            [
              { "name": "Idempotency", "version": "1", "type": "REST" }
            ]
            """);
        var mock = new OpenApiMock("idempotency-service", "/tmp/idempotency.yaml", "Idempotency", "1");

        // Act
        var matches = MicrocksImporter.CountMatchingServices(services.RootElement, mock);

        // Assert
        Assert.Equal(1, matches);
    }

    [Fact]
    public void CountMatchingServices_WithPagedObject_ShouldFindService()
    {
        // Arrange
        using var services = JsonDocument.Parse(
            """
            {
              "content": [
                { "name": "Idempotency", "version": "1", "type": "REST" }
              ]
            }
            """);
        var mock = new OpenApiMock("idempotency-service", "/tmp/idempotency.yaml", "Idempotency", "1");

        // Act
        var matches = MicrocksImporter.CountMatchingServices(services.RootElement, mock);

        // Assert
        Assert.Equal(1, matches);
    }

    [Fact]
    public void CountMatchingServices_WithEntryMissingAField_ShouldThrowAFocusedError()
    {
        // Arrange — a malformed Microcks response must not surface as KeyNotFoundException
        using var services = JsonDocument.Parse(
            """
            [
              { "name": "Idempotency", "version": "1" }
            ]
            """);
        var mock = new OpenApiMock("idempotency-service", "/tmp/idempotency.yaml", "Idempotency", "1");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => MicrocksImporter.CountMatchingServices(services.RootElement, mock));
        Assert.Contains("/api/services", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ShouldAddProjectPerComponent_WithSidecarAppId()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot);

        // Assert
        Assert.Equal("order-extractor", SidecarOptions(builder, "order-extractor").AppId);
        Assert.Equal("order-loader", SidecarOptions(builder, "order-loader").AppId);
    }

    [Fact]
    public void Apply_ShouldPointSidecarResourcesPath_AtTheComponentsOwnStagedOverlay()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot);

        // Assert — each sidecar loads its own staged set, not a shared directory.
        var expected = Path.Combine(builder.AppHostDirectory, "obj", "dapr-components", "order-extractor");
        Assert.Contains(expected, SidecarOptions(builder, "order-extractor").ResourcesPaths!);
        Assert.DoesNotContain(expected, SidecarOptions(builder, "order-loader").ResourcesPaths!);
    }

    [Fact]
    public void Apply_ShouldMakePublisherWaitForItsSubscribers()
    {
        // Arrange — order-extractor publishes order-raw; order-loader subscribes it.
        var builder = CreateBuilder();

        // Act
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot);

        // Assert — derived from the topology: the publisher waits, the subscriber does not.
        var extractor = builder.Resources.Single(r => r.Name == "order-extractor");
        Assert.Contains(
            extractor.Annotations.OfType<WaitAnnotation>(),
            w => w.Resource.Name == "order-loader");
        var loader = builder.Resources.Single(r => r.Name == "order-loader");
        Assert.DoesNotContain(
            loader.Annotations.OfType<WaitAnnotation>(),
            w => w.Resource.Name == "order-extractor");
    }

    [Fact]
    public async Task Apply_ShouldInjectRuntimeConfigEnvironment()
    {
        // Arrange
        var builder = CreateBuilder();
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot);
        var resource = builder.Resources.OfType<IResourceWithEnvironment>().Single(r => r.Name == "order-extractor");

        // Act — publish-mode evaluation: the intropy variables are plain strings, and run-mode
        // evaluation of a project resource would wait for endpoint allocation that never happens
        // outside DCP.
        var env = await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        // Assert
        Assert.Equal("order-extractor", env["INTROPY__COMPONENT"]);
        Assert.Equal(
            Path.Combine(GeneratedRoot, GeneratedArtifacts.ConfigDir, "order-extractor.intropy.json"),
            env["INTROPY__CONFIG"]);

        // The host is a local-only development composition; the environment is part of that
        // contract, not something a consumer's launch profile should have to repeat. Both
        // names: generic hosts read DOTNET_, web projects read ASPNETCORE_.
        Assert.Equal("Development", env["DOTNET_ENVIRONMENT"]);
        Assert.Equal("Development", env["ASPNETCORE_ENVIRONMENT"]);
    }

    [Fact]
    public async Task Apply_WithFilePortResolution_ShouldInjectPortRootPathForInboundPorts()
    {
        // Arrange — the extractor reads webshop (In), the loader writes erp (Out); both ports
        // resolve to local folders.
        var development = new DevelopmentManifest(
            [],
            [new PortFileResolution("webshop", "./test/webshop"), new PortFileResolution("erp", "./test/erp")]);
        var builder = CreateBuilder();
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot, development);

        // Act
        var extractor = builder.Resources.OfType<IResourceWithEnvironment>().Single(r => r.Name == "order-extractor");
        var loader = builder.Resources.OfType<IResourceWithEnvironment>().Single(r => r.Name == "order-loader");
        var extractorEnv = await extractor.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
        var loaderEnv = await loader.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        // Assert — the inbound port gets the same absolute path the binding YAML carries; the
        // outbound port gets nothing (it is reached only through the binding).
        Assert.Equal(
            Path.GetFullPath(Path.Combine(AppHostDir, "./test/webshop")),
            extractorEnv["Ports__webshop__RootPath"]);
        Assert.False(loaderEnv.ContainsKey("Ports__erp__RootPath"));
    }

    [Fact]
    public async Task Apply_WithoutFilePortResolution_ShouldInjectNoPortRootPath()
    {
        // Arrange — no development manifest: check/generate fail on unresolved ports, Apply
        // stays tolerant (the four-arg overload passes an empty manifest).
        var builder = CreateBuilder();
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot);

        // Act
        var extractor = builder.Resources.OfType<IResourceWithEnvironment>().Single(r => r.Name == "order-extractor");
        var env = await extractor.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        // Assert
        Assert.False(env.ContainsKey("Ports__webshop__RootPath"));
    }

    [Fact]
    public void Apply_WithComponentMissingItsProject_ShouldThrow()
    {
        // Arrange — order-loader has no project on disk.
        Directory.Delete(Path.Combine(_workspace, "order-loader"), recursive: true);
        var builder = CreateBuilder();

        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => IntropyAspire.Apply(builder, Topology(), GeneratedRoot));
        Assert.Contains("order-loader", ex.Message, StringComparison.Ordinal);
        Assert.Contains("No project found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectConvention_Resolve_ShouldFallBackToFlatSiblingLayout()
    {
        // Act — no src/ directory exists, so the flat layout applies.
        var path = ProjectConvention.Resolve("/repo/examples/OrderFlow.SystemHost", "order-extractor");

        // Assert
        Assert.Equal(
            Path.GetFullPath("/repo/examples/order-extractor/order-extractor.csproj"),
            path);
    }

    [Fact]
    public void ProjectConvention_Resolve_ShouldPreferSingleProjectUnderSrc()
    {
        // Arrange — the scaffold layout: <name>/src/<Project>.csproj
        var src = Directory.CreateDirectory(Path.Combine(_workspace, "order-transformer", "src")).FullName;
        var project = Path.Combine(src, "OrderTransformer.csproj");
        File.WriteAllText(project, "<Project />");

        // Act
        var path = ProjectConvention.Resolve(AppHostDir, "order-transformer");

        // Assert
        Assert.Equal(project, path);
    }

    [Fact]
    public void ProjectConvention_LocalComponentsDir_ShouldPointIntoTheIntegrationRoot()
    {
        // Act
        var dir = ProjectConvention.LocalComponentsDir("/repo/examples/OrderFlow.SystemHost", "order-extractor");

        // Assert
        Assert.Equal(
            Path.GetFullPath("/repo/examples/order-extractor/local/dapr-components"),
            dir);
    }

    [Fact]
    public async Task Apply_WithoutOtlpDeclaration_ShouldInjectNoTelemetryVariables()
    {
        // Arrange — the default path: no declaration, so intropy writes no OTEL_EXPORTER_OTLP_*
        // values and Aspire's own dashboard wiring remains the only telemetry configuration.
        var builder = CreateBuilder();
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot);
        var resource = builder.Resources.OfType<IResourceWithEnvironment>().Single(r => r.Name == "order-extractor");

        // Act
        var env = await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        // Assert
        Assert.False(env.ContainsKey("OTEL_EXPORTER_OTLP_HEADERS"));
        Assert.False(env.ContainsKey("OTEL_EXPORTER_OTLP_PROTOCOL"));
        var endpoint = env.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out var value) ? value : null;
        Assert.NotEqual("http://collector:4317", endpoint);
    }

    [Fact]
    public async Task Apply_WithOtlpDeclaration_ShouldExportComponentTelemetryToTheDeclaredSink()
    {
        // Arrange
        var builder = CreateBuilder();
        IntropyAspire.Apply(builder, OtlpTopology(), GeneratedRoot);
        var resource = builder.Resources.OfType<IResourceWithEnvironment>().Single(r => r.Name == "order-extractor");

        // Act — publish-mode evaluation: the variables are plain strings (see the runtime config
        // test above for why run-mode evaluation cannot run outside DCP).
        var env = await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        // Assert — the declaration wins over Aspire's injected dashboard endpoint.
        Assert.Equal("http://collector:4317", env["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        Assert.Equal("http/protobuf", env["OTEL_EXPORTER_OTLP_PROTOCOL"]);
        Assert.Equal("x-api-key=${OTLP_API_KEY}", env["OTEL_EXPORTER_OTLP_HEADERS"]);
    }

    [Fact]
    public async Task Apply_WithOtlpDeclaration_ShouldExportSidecarTelemetryToTheDeclaredSink()
    {
        // Arrange — the sidecar resource is not an IResourceWithEnvironment; its environment is
        // carried by the environment-callback annotation the Dapr toolkit copies onto the
        // <component>-dapr-cli executable. The declared declaration must attach exactly one.
        var builder = CreateBuilder();
        IntropyAspire.Apply(builder, OtlpTopology(), GeneratedRoot);

        // Act
        var sidecar = SidecarResource(builder, "order-extractor");
        var callback = Assert.Single(sidecar.Annotations.OfType<EnvironmentCallbackAnnotation>());
        var variables = new Dictionary<string, object>();
        await callback.Callback(new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            variables,
            CancellationToken.None));

        // Assert
        Assert.Equal("http://collector:4317", variables["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        Assert.Equal("http/protobuf", variables["OTEL_EXPORTER_OTLP_PROTOCOL"]);
    }

    [Fact]
    public async Task Apply_WithoutOtlpDeclaration_ShouldAttachNoSidecarEnvironmentCallback()
    {
        // Arrange
        var builder = CreateBuilder();
        IntropyAspire.Apply(builder, Topology(), GeneratedRoot);

        // Act & Assert
        Assert.Empty(SidecarResource(builder, "order-extractor").Annotations.OfType<EnvironmentCallbackAnnotation>());
    }

    private static IResource SidecarResource(IDistributedApplicationBuilder builder, string componentName) =>
        builder.Resources.Single(r => r.Name == componentName)
            .Annotations.OfType<DaprSidecarAnnotation>().Single().Sidecar;

    private static SystemTopology OtlpTopology()
    {
        var builder = SystemBuilder.Create("order-flow");
        builder.AddExtractor("order-extractor").From(s_webshop).Publishes(s_raw);
        builder.AddLoader("order-loader").Subscribes(s_raw).To(s_erp);
        builder.Otlp("http://collector:4317")
            .WithProtocol(Intropy.Topology.Model.OtlpProtocol.HttpProtobuf)
            .WithHeader("x-api-key", "${OTLP_API_KEY}");
        return builder.Build();
    }

    [Fact]
    public async Task OtlpVariables_ShouldComposeTheDeclaredVariables()
    {
        // Arrange
        var otlp = OtlpTopology().Otlp!;

        // Act — the exact callback Wire attaches to the sidecar, evaluated via the same
        // annotation type the Dapr toolkit propagates to the sidecar executable.
        var variables = new Dictionary<string, object>();
        await IntropyAspire.OtlpVariables(otlp).Callback(new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            variables,
            CancellationToken.None));

        // Assert
        Assert.Equal("http://collector:4317", variables["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        Assert.Equal("http/protobuf", variables["OTEL_EXPORTER_OTLP_PROTOCOL"]);
        Assert.Equal("x-api-key=${OTLP_API_KEY}", variables["OTEL_EXPORTER_OTLP_HEADERS"]);
    }

    [Fact]
    public void OtlpEnvironment_WithNoHeaders_ShouldEmitNoHeadersVariable()
    {
        // Arrange
        var otlp = OtlpTopology().Otlp! with { Headers = new Dictionary<string, string>() };

        // Act
        var env = OtlpEnvironment.For(otlp);

        // Assert
        Assert.False(env.ContainsKey("OTEL_EXPORTER_OTLP_HEADERS"));
    }
    private static DaprSidecarOptions SidecarOptions(IDistributedApplicationBuilder builder, string componentName)
    {
        var resource = builder.Resources.Single(r => r.Name == componentName);
        var sidecar = Assert.Single(resource.Annotations.OfType<DaprSidecarAnnotation>()).Sidecar;
        return Assert.Single(((IResource)sidecar).Annotations.OfType<DaprSidecarOptionsAnnotation>()).Options;
    }
}
