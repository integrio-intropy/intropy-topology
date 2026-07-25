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
/// with a Dapr sidecar on its own staged component overlay, and RabbitMQ behind pub/sub —
/// then runs it under DCP, subscribers before their publishers. Scheduled components are
/// re-run on their cron ticks by the <see cref="ComponentScheduler"/> (CronJob emulation,
/// with a "Run now" dashboard command). Aspire never sees the topology; it receives
/// ordinary resources.
/// </summary>
public static class IntropyAspire
{
    /// <summary>Fixed host port for the local RabbitMQ backend (generated pub/sub YAML points here).</summary>
    internal const int RabbitMqPort = 5672;

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

        var discovered = SystemDiscovery.Discover(assembly);

        var generatedRoot = Path.Combine(
            Path.GetTempPath(), "intropy-aspire", $"{discovered.Topology.SystemName}-{Guid.NewGuid():N}");
        TopologyGenerator.Generate(discovered.Topology).WriteTo(generatedRoot);

        var builder = DistributedApplication.CreateBuilder(args);
        Apply(builder, discovered.Topology, generatedRoot);

        var app = builder.Build();
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Translates the topology into Aspire resources on <paramref name="builder"/>. Separated from
    /// <see cref="RunAsync"/> so the built model can be inspected in tests without running DCP.
    /// </summary>
    internal static void Apply(
        IDistributedApplicationBuilder builder, SystemTopology topology, string generatedRoot)
    {
        var componentsPath = Path.Combine(generatedRoot, GeneratedArtifacts.ComponentsDir);
        var configDir = Path.Combine(generatedRoot, GeneratedArtifacts.ConfigDir);

        // A plain RabbitMQ container rather than AddRabbitMQ: the generated pub/sub YAML speaks
        // default guest/guest to localhost:5672, while AddRabbitMQ injects generated credentials.
        var rabbitmq = builder
            .AddContainer("rabbitmq", "docker.io/library/rabbitmq", "4")
            .WithEndpoint(port: RabbitMqPort, targetPort: RabbitMqPort);
        WithTcpReadiness(builder, rabbitmq, RabbitMqPort);

        // Everything a sidecar dials during component init. Sidecars must wait for these to be
        // *ready* (not merely running) — daprd treats a failed component init as fatal, so racing
        // a still-starting backend kills the sidecar.
        var backends = new List<IResourceBuilder<IResource>> { rabbitmq };
        var backendProbes = new List<TcpReadyHealthCheck> { new("localhost", RabbitMqPort) };

        // Filled after the component loop; the hook closure only reads it at runtime.
        var sidecarWaits = new Dictionary<string, string[]>(StringComparer.Ordinal);

        // The Dapr toolkit materializes each sidecar as a separate "<component>-dapr-cli" executable
        // that does not carry wait annotations, so WaitFor cannot gate it. Hold its start here until
        // every backend port answers and, for publishers, until every subscriber sidecar is running.
        builder.Eventing.Subscribe<BeforeResourceStartedEvent>(async (evt, cancellationToken) =>
        {
            if (!evt.Resource.Name.EndsWith("-dapr-cli", StringComparison.Ordinal))
            {
                return;
            }

            foreach (var probe in backendProbes)
            {
                await probe.WaitUntilReadyAsync(TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
            }

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

            Wire(project, component, stagedPath, configDir, backends);
            resources[component.Name] = project;
            waiters[component.Name] = dependency => project.WaitFor(dependency);
        }

        RegisterScheduler(builder, topology);

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
                sidecarWaits[$"{publisher}-dapr-cli"] =
                    [.. materialized.Select(s => $"{s}-dapr-cli")];
            }
        }
    }

    /// <summary>
    /// Registers the <see cref="ComponentScheduler"/> when any component declares a schedule.
    /// Keyed off <c>Schedule</c>, not the component kind — the scheduler is kind-blind.
    /// </summary>
    private static void RegisterScheduler(IDistributedApplicationBuilder builder, SystemTopology topology)
    {
        var scheduled = topology.Components
            .Where(c => c.Schedule is not null)
            .Select(c => new ScheduledComponent(c.Name, c.Schedule!))
            .ToArray();
        if (scheduled.Length == 0)
        {
            return;
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IReadOnlyList<ScheduledComponent>>(scheduled);
        builder.Services.AddSingleton<IResourceLifecycle, AspireResourceLifecycle>();
        builder.Services.AddSingleton<ComponentScheduler>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ComponentScheduler>());
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

    private static void WithTcpReadiness(
        IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> container, int port)
    {
        var key = $"{container.Resource.Name}-tcp-ready";
        builder.Services.AddHealthChecks().AddCheck(key, new TcpReadyHealthCheck("localhost", port));
        container.WithHealthCheck(key);
    }

    private static void Wire<T>(
        IResourceBuilder<T> resource,
        ComponentModel component,
        string resourcesPath,
        string configDir,
        IReadOnlyList<IResourceBuilder<IResource>> backends)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        resource
            .WithDaprSidecar(sidecar =>
            {
                sidecar.WithOptions(new DaprSidecarOptions
                {
                    AppId = component.Name,
                    ResourcesPaths = [resourcesPath],
                });
                foreach (var backend in backends)
                {
                    sidecar.WaitFor(backend);
                }
            })
            .WithEnvironment("INTROPY__COMPONENT", component.Name)
            .WithEnvironment("INTROPY__CONFIG", Path.Combine(configDir, $"{component.Name}.intropy.json"));

        foreach (var backend in backends)
        {
            resource.WaitFor(backend);
        }
    }
}
