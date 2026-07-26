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
