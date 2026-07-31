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
/// The Aspire run-backend. Discovers a system's validated topology, generates its Dapr
/// components, and translates the model into Aspire resources — one project per component
/// with a Dapr sidecar on its own staged component overlay, and Redis behind pub/sub —
/// then runs it under DCP, subscribers before their publishers. Scheduled components are
/// re-run on their cron ticks by the <see cref="ComponentScheduler"/> (CronJob emulation,
/// with a "Run now" dashboard command). Aspire never sees the topology; it receives
/// ordinary resources.
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
            TopologyGenerator.Generate(discovered.Topology, development).WriteTo(generatedRoot);

            Apply(builder, discovered.Topology, generatedRoot, development);

            var app = builder.Build();
            await app.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is TopologyValidationException or DevelopmentValidationException or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// Translates the topology into Aspire resources on <paramref name="builder"/>. Separated from
    /// <see cref="RunAsync"/> so the built model can be inspected in tests without running DCP.
    /// </summary>
    internal static void Apply(
        IDistributedApplicationBuilder builder, SystemTopology topology, string generatedRoot) =>
        Apply(builder, topology, generatedRoot, new DevelopmentManifest([]));

    internal static void Apply(
        IDistributedApplicationBuilder builder, SystemTopology topology, string generatedRoot, DevelopmentManifest development)
    {
        var componentsPath = Path.Combine(generatedRoot, GeneratedArtifacts.ComponentsDir);
        var configDir = Path.Combine(generatedRoot, GeneratedArtifacts.ConfigDir);

        // A plain Redis container rather than AddRedis: the generated pub/sub YAML speaks
        // passwordless to localhost:6380, while AddRedis injects a generated password.
        EnsureHostPortAvailable(RedisPort);
        var redis = builder
            .AddContainer("redis", "docker.io/library/redis", "7")
            .WithEndpoint(port: RedisPort, targetPort: 6379);
        // Everything a sidecar dials during component init. Sidecars must wait for Redis to be
        // *ready* (not merely running) — daprd treats a failed component init as fatal, so racing
        // a still-starting backend kills the sidecar. localhost:6380 is DCP's endpoint proxy,
        // which accepts connections from host start, so the probe must demand a PONG rather
        // than settle for an open socket.
        var redisReadiness = new TcpReadyHealthCheck("localhost", RedisPort, TcpReadyHealthCheck.RedisHandshake);
        WithTcpReadiness(builder, redis, redisReadiness);

        MockReadiness? mockReadiness = null;
        IResourceBuilder<ContainerResource>? microcks = null;
        if (development.Mocks.Count > 0)
        {
            EnsureHostPortAvailable(MicrocksPort, "Microcks");
            microcks = builder
                .AddContainer("microcks", "quay.io/microcks/microcks-uber", "1.13.0")
                .WithEndpoint(name: "http", scheme: "http", port: MicrocksPort, targetPort: 8080)
                .WithHttpHealthCheck("/api/health");
            mockReadiness = new MockReadiness(development);
            builder.Services.AddSingleton(development);
            builder.Services.AddSingleton(mockReadiness);
            builder.Services.AddHttpClient(nameof(MicrocksImporter));
            builder.Services.AddSingleton<MicrocksImporter>();
            microcks.OnResourceReady(async (_, evt, cancellationToken) =>
            {
                var importer = evt.Services.GetRequiredService<MicrocksImporter>();
                await importer.ImportAsync(cancellationToken).ConfigureAwait(false);
            });
        }

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
            if (component.Schedule is null)
            {
                // The endpoint makes Aspire allocate a port and inject ASPNETCORE_URLS (without it
                // every Kestrel binds the 5000 default) and gives the Dapr sidecar its app-port.
                // A scheduled component is a run-to-completion job that receives no inbound
                // delivery, so it gets neither.
                project = project.WithHttpEndpoint();
            }
            else
            {
                AddRunNowCommand(project, component.Name);
            }

            Wire(project, component, stagedPath, configDir, redis, microcks);
            resources[component.Name] = project;
            waiters[component.Name] = dependency => project.WaitFor(dependency);
        }

        RegisterDaprSidecarRecovery(builder, topology, redisReadiness);
        RegisterScheduler(builder, topology, mockReadiness);

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
        var precedence = SubscriberPrecedence(topology);
        if (HasCycle(precedence))
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
    /// Registers the <see cref="ComponentScheduler"/> when any component declares a schedule.
    /// Keyed off <c>Schedule</c>, not the component kind — the scheduler is kind-blind.
    /// </summary>
    private static void RegisterScheduler(IDistributedApplicationBuilder builder, SystemTopology topology, MockReadiness? mockReadiness)
    {
        var scheduled = topology.Components
            .Where(c => c.Schedule is not null)
            .Select(c => new ScheduledComponent(c.Name, c.Schedule!, c.Uses.Count == 0 ? null : c.Uses))
            .ToArray();
        if (scheduled.Length == 0)
        {
            return;
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IReadOnlyList<ScheduledComponent>>(scheduled);
        if (mockReadiness is not null)
        {
            builder.Services.AddSingleton(mockReadiness);
        }
        builder.Services.TryAddSingleton<IResourceLifecycle, AspireResourceLifecycle>();
        builder.Services.AddSingleton<ComponentScheduler>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ComponentScheduler>());
    }

    /// <summary>
    /// Registers recovery for long-running components' Dapr sidecars. The normal pre-start gate
    /// prevents the common race; recovery covers the remaining interval where a backend passes
    /// TCP readiness but daprd's component initialization still fails. Scheduled components'
    /// sidecars are excluded: the scheduler owns their stop/start lifecycle.
    /// </summary>
    private static void RegisterDaprSidecarRecovery(
        IDistributedApplicationBuilder builder, SystemTopology topology, IBackendReadiness backendReadiness)
    {
        var schedulerOwned = topology.Components
            .Where(c => c.Schedule is not null)
            .Select(c => DaprSidecarIdentity.CliName(c.Name))
            .ToHashSet(StringComparer.Ordinal);
        builder.Services.AddSingleton(new DaprSidecarRecoveryOptions(schedulerOwned));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IResourceLifecycle, AspireResourceLifecycle>();
        builder.Services.AddSingleton<IResourceStateMonitor, AspireResourceStateMonitor>();
        builder.Services.AddSingleton<IBackendReadiness>(backendReadiness);
        builder.Services.AddSingleton<DaprSidecarRecovery>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<DaprSidecarRecovery>());
    }

    /// <summary>
    /// A dashboard command that triggers a scheduled component immediately, through the same
    /// path as a cron tick — including the skip when the previous run is still in progress.
    /// </summary>
    private static void AddRunNowCommand(IResourceBuilder<ProjectResource> project, string componentName)
    {
        project.WithCommand(
            name: "intropy-run-now",
            displayName: "Run now",
            executeCommand: async context =>
            {
                var scheduler = context.ServiceProvider.GetRequiredService<ComponentScheduler>();
                await scheduler.TriggerAsync(componentName, "manual run", context.CancellationToken)
                    .ConfigureAwait(false);
                return CommandResults.Success();
            },
            commandOptions: new CommandOptions { IconName = "Play", IconVariant = IconVariant.Filled });
    }

    /// <summary>
    /// Which components each publisher must wait for: every subscriber of every topic it
    /// publishes. Self-loops (a component consuming its own topic) are not orderable edges.
    /// </summary>
    private static Dictionary<string, HashSet<string>> SubscriberPrecedence(SystemTopology topology)
    {
        var precedence = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var topic in topology.Topics)
        {
            foreach (var publisher in topic.Publishers)
            {
                foreach (var subscriber in topic.Subscribers)
                {
                    if (string.Equals(publisher, subscriber, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!precedence.TryGetValue(publisher, out var subscribers))
                    {
                        precedence[publisher] = subscribers = new HashSet<string>(StringComparer.Ordinal);
                    }

                    subscribers.Add(subscriber);
                }
            }
        }

        return precedence;
    }

    private static bool HasCycle(Dictionary<string, HashSet<string>> edges)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 = visiting, 2 = done

        bool Visit(string node)
        {
            if (state.TryGetValue(node, out var seen))
            {
                return seen == 1;
            }

            state[node] = 1;
            if (edges.TryGetValue(node, out var next) && next.Any(Visit))
            {
                return true;
            }

            state[node] = 2;
            return false;
        }

        return edges.Keys.Any(Visit);
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

    private static void Wire(
        IResourceBuilder<ProjectResource> project,
        ComponentModel component,
        string resourcesPath,
        string configDir,
        IResourceBuilder<ContainerResource> redis,
        IResourceBuilder<ContainerResource>? microcks)
    {
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
            .WaitFor(redis);
        if (microcks is not null && component.Uses.Count > 0)
        {
            project.WaitFor(microcks);
        }
    }
}
