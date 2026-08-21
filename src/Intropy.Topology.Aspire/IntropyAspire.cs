using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Intropy.Topology.Generation;
using Intropy.Topology.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intropy.Topology.Aspire;

/// <summary>
/// Runs a discovered topology as a local Aspire application with Dapr sidecars. Components
/// start subscribers before publishers when the topic graph is acyclic.
/// </summary>
public static class IntropyAspire
{
    /// <summary>
    /// Fixed host port for the local Redis backend (generated pub/sub YAML points here).
    /// 6380 rather than the Redis default: `dapr init` publishes its own dapr_redis on 6379.
    /// </summary>
    internal const int RedisPort = 6380;

    /// <summary>Fixed host port for the local Microcks Uber backend.</summary>
    internal const int MicrocksPort = 8585;

    /// <summary>
    /// The local Microcks origin — this package's composition of <see cref="MicrocksPort"/>.
    /// Must agree with <c>LocalMockEndpoints.Origin</c> in the Generation package: the
    /// <c>topology.intropy.io/v1</c> graph contract forces that package to know the origin
    /// too. Asserted by test; keep both in sync.
    /// </summary>
    internal const string MicrocksOrigin = "http://localhost:8585";

    /// <summary>
    /// Discovers the system in <paramref name="assembly"/>, generates its artifacts to a temp folder,
    /// builds the Aspire resource graph, and runs it. Requires Docker and the Dapr CLI to be present.
    /// </summary>
    /// <param name="assembly">The assembly declaring the <see cref="ISystemDefinition"/>.</param>
    /// <param name="args">Command-line args passed through to the distributed application builder.</param>
    /// <returns>A process exit code.</returns>
    public static async Task<int> RunAsync(Assembly assembly, string[] args)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(args);

        try
        {
            var discovered = SystemDiscovery.Discover(assembly);
            var builder = DistributedApplication.CreateBuilder(args);
            var development = DevelopmentDiscovery.Discover(assembly, discovered.Topology, builder.AppHostDirectory);

            var generatedRoot = Path.Combine(
                Path.GetTempPath(), "intropy-aspire", $"{discovered.Topology.SystemName}-{Guid.NewGuid():N}");
            TopologyGenerator.Generate(discovered.Topology, development, builder.AppHostDirectory).WriteTo(generatedRoot);

            Apply(builder, discovered.Topology, generatedRoot, development);

            var app = builder.Build();
            await app.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is IntropyException or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// Whether a component kind runs to completion (one sweep, then exit) rather than as a
    /// resident service. Sound because each kind has exactly one legitimate host — the
    /// framework's <c>RunToCompletionRunner</c> for extractors and
    /// <c>TransactionalIntegrationRunner</c> for transactional integrations, both of which shut
    /// the Dapr sidecar down in a <c>finally</c> so the project/sidecar pair reaches its terminal
    /// state together. Deliberately derived from kind rather than modeled: the topology records
    /// edges, not workload shape. If a kind ever gains a lifetime its edges do not imply, this
    /// derivation must become an explicit model fact.
    /// </summary>
    internal static bool IsRunToCompletion(ComponentKind kind) =>
        kind is ComponentKind.Extractor or ComponentKind.TransactionalIntegration;

    /// <summary>
    /// Translates the topology into Aspire resources on <paramref name="builder"/>. Separated from
    /// <see cref="RunAsync"/> so the built model can be inspected in tests without running DCP.
    /// </summary>
    internal static void Apply(
        IDistributedApplicationBuilder builder, SystemTopology topology, string generatedRoot) =>
        Apply(builder, topology, generatedRoot, new DevelopmentManifest([], []));

    internal static void Apply(
        IDistributedApplicationBuilder builder, SystemTopology topology, string generatedRoot, DevelopmentManifest development)
    {
        var componentsPath = Path.Combine(generatedRoot, GeneratedArtifacts.ComponentsDir);
        var configDir = Path.Combine(generatedRoot, GeneratedArtifacts.ConfigDir);

        var (redis, redisReadiness) = AddRedisBackend(builder);
        var (microcks, mockReadiness) = AddMicrocksBackend(builder, development);

        // Filled after the component loop; the hook closure only reads it at runtime.
        var sidecarWaits = new Dictionary<string, string[]>(StringComparer.Ordinal);

        // The Dapr toolkit materializes each sidecar as a separate "<component>-dapr-cli" executable
        // that does not carry wait annotations, so WaitFor cannot gate it. Hold its start here until
        // every backend port answers and, for publishers, until every subscriber sidecar is running.
        builder.Eventing.Subscribe<BeforeResourceStartedEvent>(async (evt, cancellationToken) =>
        {
            var isSidecar = DaprSidecarIdentity.IsCli(evt.Resource.Name);
            var componentName = isSidecar
                ? evt.Resource.Name[..^"-dapr-cli".Length]
                : evt.Resource.Name;
            var component = topology.Components.SingleOrDefault(candidate => candidate.Name == componentName);
            if (component is null)
            {
                return;
            }

            if (mockReadiness is not null)
            {
                await mockReadiness.WaitForAsync(component.Uses, TimeSpan.FromMinutes(3), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!isSidecar)
            {
                return;
            }

            await redisReadiness.WaitUntilReadyAsync(TimeSpan.FromMinutes(3), cancellationToken)
                .ConfigureAwait(false);
            if (sidecarWaits.TryGetValue(evt.Resource.Name, out var subscriberSidecars))
            {
                var notifications = evt.Services.GetRequiredService<ResourceNotificationService>();
                foreach (var sidecar in subscriberSidecars)
                {
                    await notifications
                        .WaitForResourceAsync(sidecar, KnownResourceStates.Running, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        });

        var resources = new Dictionary<string, IResourceBuilder<IResource>>(StringComparer.Ordinal);
        var waiters = new Dictionary<string, Action<IResourceBuilder<IResource>>>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach (var component in topology.Components)
        {
            // Each integration gets its own staged component set: its local files plus the
            // generated system overlay, at a stable inspectable path under obj/.
            var stagedPath = ComponentOverlay.Stage(builder.AppHostDirectory, component.Name, componentsPath);

            var projectPath = ProjectConvention.Resolve(builder.AppHostDirectory, component.Name);
            if (!File.Exists(projectPath))
            {
                unresolved.Add($"{component.Name} (expected {projectPath})");
                continue;
            }

            var project = builder.AddProject(component.Name, projectPath);
            // The endpoint makes Aspire allocate a port and inject ASPNETCORE_URLS (without it
            // every Kestrel binds the 5000 default) and gives the Dapr sidecar its app-port.
            project = project.WithHttpEndpoint();

            Wire(project, component, stagedPath, configDir, redis, microcks);
            resources[component.Name] = project;
            waiters[component.Name] = dependency => project.WaitFor(dependency);
        }

        RegisterDaprSidecarRecovery(builder, redisReadiness, RunToCompletionSidecarsFor(topology));

        if (unresolved.Count > 0)
        {
            throw new InvalidOperationException(
                $"No project found for these components by folder convention: {string.Join(", ", unresolved)}. " +
                "Create the project next to the SystemHost.");
        }

        // Subscribers start first, so a publisher's first message finds a registered
        // subscription. Nobody declares this: it is derived from who publishes and who
        // subscribes each topic. Publish/subscribe cycles cannot be ordered — skip entirely
        // rather than deadlock.
        var precedence = StartupOrdering.SubscriberPrecedence(topology);
        if (StartupOrdering.HasCycle(precedence))
        {
            Console.Error.WriteLine(
                "intropy: the topology's topics form a publish/subscribe cycle; " +
                "skipping subscriber-first startup ordering.");
            return;
        }

        foreach (var (publisher, subscribers) in precedence)
        {
            if (!waiters.TryGetValue(publisher, out var wait))
            {
                continue;
            }

            var materialized = subscribers.Where(resources.ContainsKey).ToArray();
            foreach (var subscriber in materialized)
            {
                wait(resources[subscriber]);
            }

            if (materialized.Length > 0)
            {
                sidecarWaits[DaprSidecarIdentity.CliName(publisher)] =
                    [.. materialized.Select(DaprSidecarIdentity.CliName)];
            }
        }
    }

    /// <summary>
    /// The sidecar executables exempt from recovery: every run-to-completion component's
    /// sidecar (see <see cref="IsRunToCompletion"/>). Derived from the topology's kinds — an
    /// unresolved component (no project on disk) still gets an entry; its sidecar never
    /// exists, so the name is simply never observed.
    /// </summary>
    internal static RunToCompletionSidecars RunToCompletionSidecarsFor(SystemTopology topology) =>
        new(topology.Components
            .Where(c => IsRunToCompletion(c.Kind))
            .Select(c => DaprSidecarIdentity.CliName(c.Name))
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// Registers recovery for components' Dapr sidecars. The normal pre-start gate
    /// prevents the common race; recovery covers the remaining interval where a backend passes
    /// TCP readiness but daprd's component initialization still fails.
    /// </summary>
    private static void RegisterDaprSidecarRecovery(
        IDistributedApplicationBuilder builder,
        IBackendReadiness backendReadiness,
        RunToCompletionSidecars runToCompletionSidecars)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IResourceLifecycle, AspireResourceLifecycle>();
        builder.Services.AddSingleton<IResourceStateMonitor, AspireResourceStateMonitor>();
        builder.Services.AddSingleton<IBackendReadiness>(backendReadiness);
        builder.Services.AddSingleton(runToCompletionSidecars);
        builder.Services.AddSingleton<DaprSidecarRecovery>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<DaprSidecarRecovery>());
    }

    /// <summary>
    /// A plain Redis container rather than AddRedis: the generated pub/sub YAML speaks
    /// passwordless to localhost:6380, while AddRedis injects a generated password.
    /// </summary>
    private static (IResourceBuilder<ContainerResource> Redis, TcpReadyHealthCheck Readiness) AddRedisBackend(
        IDistributedApplicationBuilder builder)
    {
        EnsureHostPortAvailable(RedisPort);
        var redis = builder
            .AddContainer("redis", "docker.io/library/redis", "7")
            .WithEndpoint(port: RedisPort, targetPort: 6379);
        // Everything a sidecar dials during component init. Sidecars must wait for Redis to be
        // *ready* (not merely running) — daprd treats a failed component init as fatal, so racing
        // a still-starting backend kills the sidecar. localhost:6380 is DCP's endpoint proxy,
        // which accepts connections from host start, so the probe must demand a PONG rather
        // than settle for an open socket.
        var readiness = new TcpReadyHealthCheck("localhost", RedisPort, TcpReadyHealthCheck.RedisHandshake);
        WithTcpReadiness(builder, redis, readiness);
        return (redis, readiness);
    }

    /// <summary>
    /// The local Microcks Uber backend and its contract importer, when the development
    /// manifest declares mocks; both results are null otherwise.
    /// </summary>
    private static (IResourceBuilder<ContainerResource>? Microcks, MockReadiness? Readiness) AddMicrocksBackend(
        IDistributedApplicationBuilder builder, DevelopmentManifest development)
    {
        if (development.Mocks.Count == 0)
        {
            return (null, null);
        }

        EnsureHostPortAvailable(MicrocksPort, "Microcks");
        var microcks = builder
            .AddContainer("microcks", "quay.io/microcks/microcks-uber", "1.13.0")
            .WithEndpoint(name: "http", scheme: "http", port: MicrocksPort, targetPort: 8080)
            .WithHttpHealthCheck("/api/health");
        var readiness = new MockReadiness(development);
        builder.Services.AddSingleton(development);
        builder.Services.AddSingleton(readiness);
        builder.Services.AddHttpClient(nameof(MicrocksImporter));
        builder.Services.AddSingleton<MicrocksImporter>();
        microcks.OnResourceReady(async (_, evt, cancellationToken) =>
        {
            var importer = evt.Services.GetRequiredService<MicrocksImporter>();
            await importer.ImportAsync(cancellationToken).ConfigureAwait(false);
        });
        return (microcks, readiness);
    }

    private static void EnsureHostPortAvailable(int port, string backend = "Redis")
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"{backend} requires host port {port}, but it is already in use. Stop the conflicting process and retry.", ex);
        }
    }

    private static void WithTcpReadiness(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ContainerResource> container,
        TcpReadyHealthCheck readiness)
    {
        var key = $"{container.Resource.Name}-tcp-ready";
        builder.Services.AddHealthChecks().AddCheck(key, readiness);
        container.WithHealthCheck(key);
    }

    /// <summary>
    /// Wires a component's project to its Dapr sidecar, runtime config, and backends. The host is
    /// a local-only development composition, so every component runs with the Development
    /// environment as part of that contract — not left to per-AppHost launch profiles. Both
    /// variable names are set: generic-host services read DOTNET_ENVIRONMENT, web projects read
    /// ASPNETCORE_ENVIRONMENT, and the component model does not distinguish them.
    /// </summary>
    private static void Wire(
        IResourceBuilder<ProjectResource> project,
        ComponentModel component,
        string resourcesPath,
        string configDir,
        IResourceBuilder<ContainerResource> redis,
        IResourceBuilder<ContainerResource>? microcks)
    {
        const string development = "Development";
        project
            .WithDaprSidecar(sidecar =>
            {
                sidecar.WithOptions(new DaprSidecarOptions
                {
                    AppId = component.Name,
                    ResourcesPaths = [resourcesPath],
                });
                sidecar.WaitFor(redis);
            })
            .WithEnvironment("INTROPY__COMPONENT", component.Name)
            .WithEnvironment("INTROPY__CONFIG", Path.Combine(configDir, $"{component.Name}.intropy.json"))
            .WithEnvironment("DOTNET_ENVIRONMENT", development)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", development)
            .WaitFor(redis);
        if (microcks is not null && component.Uses.Count > 0)
        {
            project.WaitFor(microcks);
        }
    }
}
