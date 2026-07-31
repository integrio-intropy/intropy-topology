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
    private static readonly ConnectorRef s_webshop = ConnectorRef.Define("webshop", Transport.File("./test/webshop"));
    private static readonly ConnectorRef s_erp = ConnectorRef.Define("erp", Transport.File("./test/erp"));

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
    public void Apply_WithDevelopmentMocks_ShouldAddOneMicrocksResource()
    {
        // Arrange
        var builder = CreateBuilder();
        var development = new DevelopmentManifest([
            new OpenApiMock("idempotency-service", "/tmp/idempotency.yaml", "Idempotency", "1")]);

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

    private static DaprSidecarOptions SidecarOptions(IDistributedApplicationBuilder builder, string componentName)
    {
        var resource = builder.Resources.Single(r => r.Name == componentName);
        var sidecar = Assert.Single(resource.Annotations.OfType<DaprSidecarAnnotation>()).Sidecar;
        return Assert.Single(((IResource)sidecar).Annotations.OfType<DaprSidecarOptionsAnnotation>()).Options;
    }
}
