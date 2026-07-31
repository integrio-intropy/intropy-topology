using System.Net;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Intropy.Topology;
using Intropy.Topology.Generation;
using Intropy.Topology.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Intropy.Topology.Aspire.Test;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AspireDaprFactAttribute : FactAttribute
{
    public AspireDaprFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("INTROPY_RUN_ASPIRE_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set INTROPY_RUN_ASPIRE_INTEGRATION=1 after Docker and `dapr init` are ready to run Aspire+Dapr integration tests.";
        }
    }
}

[Collection("RedisPort")]
public sealed class AspireDaprIntegrationTests : IAsyncDisposable
{
    private sealed record RawOrder(string OrderNumber);

    private static readonly TopicRef<RawOrder> s_raw = TopicRef<RawOrder>.Define("pubsub-a", "order-raw");
    private static readonly ServiceRef s_idempotency = ServiceRef.Define("idempotency-service");
    private readonly string _workspace = Directory.CreateTempSubdirectory("intropy-aspire-dapr-").FullName;

    [AspireDaprFact]
    [Trait("Category", "Integration")]
    public async Task Apply_WithDaprInstalled_ShouldStartThePinnedToolkitSidecars()
    {
        // Arrange
        var appHostDirectory = Directory.CreateDirectory(Path.Combine(_workspace, "system-host")).FullName;
        WriteWebProject("order-extractor");
        WriteWebProject("order-loader");
        var generatedRoot = Path.Combine(_workspace, "generated");
        TopologyGenerator.Generate(Topology()).WriteTo(generatedRoot);
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            ProjectDirectory = appHostDirectory,
        });
        ConfigureAspireToolchain(builder);
        IntropyAspire.Apply(builder, Topology(), generatedRoot);

        await using var application = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            // Act
            await application.StartAsync(timeout.Token);
            var notifications = application.Services.GetRequiredService<ResourceNotificationService>();
            await notifications.WaitForResourceAsync(
                DaprSidecarIdentity.CliName("order-extractor"), KnownResourceStates.Running, timeout.Token);
            await notifications.WaitForResourceAsync(
                DaprSidecarIdentity.CliName("order-loader"), KnownResourceStates.Running, timeout.Token);

            // Assert — the toolkit materializes executable sidecars using the convention isolated
            // by DaprSidecarIdentity. This is intentionally a live compatibility test, not a
            // replacement for Dapr's own component/app-channel health endpoint.
        }
        finally
        {
            await application.StopAsync(CancellationToken.None);
            await WaitForPortReleaseAsync(IntropyAspire.RedisPort);
        }
    }

    [AspireDaprFact]
    [Trait("Category", "Integration")]
    public async Task Apply_WithDevelopmentMock_ShouldImportContractBeforeStartingConsumer()
    {
        // Arrange
        var appHostDirectory = Directory.CreateDirectory(Path.Combine(_workspace, "system-host")).FullName;
        WriteWebProject("order-extractor");
        WriteWebProject("order-loader");
        var artifactPath = Path.Combine(_workspace, "idempotency-service.openapi.yaml");
        await File.WriteAllTextAsync(
            artifactPath,
            """
            openapi: 3.0.3
            info:
              title: Idempotency Service
              version: 1.0.0
            paths:
              /status:
                post:
                  responses:
                    '200':
                      description: Processing may proceed.
            """);
        var topology = TopologyWithService();
        var development = new DevelopmentManifest([
            new OpenApiMock("idempotency-service", artifactPath, "Idempotency Service", "1.0.0")]);
        var generatedRoot = Path.Combine(_workspace, "generated");
        TopologyGenerator.Generate(topology, development).WriteTo(generatedRoot);
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            ProjectDirectory = appHostDirectory,
        });
        ConfigureAspireToolchain(builder);
        IntropyAspire.Apply(builder, topology, generatedRoot, development);

        await using var application = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        try
        {
            // Act
            await application.StartAsync(timeout.Token);
            var notifications = application.Services.GetRequiredService<ResourceNotificationService>();
            await notifications.WaitForResourceAsync("order-loader", KnownResourceStates.Running, timeout.Token);

            // Assert — the consumer cannot start until its OpenAPI contract has been imported.
        }
        finally
        {
            await application.StopAsync(CancellationToken.None);
            await WaitForPortReleaseAsync(IntropyAspire.RedisPort);
            await WaitForPortReleaseAsync(IntropyAspire.MicrocksPort);
        }
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_workspace, recursive: true);
        return ValueTask.CompletedTask;
    }

    private static SystemTopology Topology()
    {
        var system = SystemBuilder.Create("order-flow");
        system.AddExtractor("order-extractor").Publishes(s_raw);
        system.AddLoader("order-loader").Subscribes(s_raw);
        return system.Build();
    }

    private static SystemTopology TopologyWithService()
    {
        var system = SystemBuilder.Create("order-flow");
        system.AddExtractor("order-extractor").Publishes(s_raw);
        system.AddLoader("order-loader").Subscribes(s_raw).Uses(s_idempotency);
        return system.Build();
    }

    private static async Task WaitForPortReleaseAsync(int port)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return;
            }
            catch (SocketException) when (!timeout.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
        }
    }

    private static void ConfigureAspireToolchain(IDistributedApplicationBuilder builder)
    {
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var dcp = FindTool(packagesRoot, "aspire.hosting.orchestration.*", "dcp");
        var dashboard = FindTool(packagesRoot, "aspire.dashboard.sdk.*", "Aspire.Dashboard");

        builder.Configuration["DcpPublisher:CliPath"] = dcp;
        builder.Configuration["DcpPublisher:DashboardPath"] = dashboard;
    }

    private static string FindTool(string packagesRoot, string packagePattern, string toolName)
    {
        var tool = Directory.EnumerateDirectories(packagesRoot, packagePattern)
            .SelectMany(package => Directory.EnumerateFiles(package, toolName, SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .LastOrDefault();
        return tool ?? throw new InvalidOperationException(
            $"The Aspire tool '{toolName}' is unavailable under {packagesRoot}; restore the AppHost toolchain first.");
    }

    private void WriteWebProject(string componentName)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_workspace, componentName)).FullName;
        File.WriteAllText(
            Path.Combine(directory, componentName + ".csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(directory, "Program.cs"),
            """
            var app = WebApplication.CreateBuilder(args).Build();
            app.MapGet("/", () => "ok");
            app.Run();
            """);
    }
}

[CollectionDefinition("RedisPort", DisableParallelization = true)]
public sealed class AspireDaprIntegrationCollection;
