using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Intropy.Topology;
using Intropy.Topology.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Intropy.Topology.Aspire.Test;

[Collection("RedisPort")]
public sealed class SchedulingApplyTests : IDisposable
{
    private sealed record RawOrder(string OrderNumber);

    private static readonly TopicRef<RawOrder> s_raw = TopicRef<RawOrder>.Define("pubsub-a", "order-raw");

    private const string GeneratedRoot = "/tmp/intropy-aspire-scheduling-test";

    private readonly string _workspace = Directory.CreateTempSubdirectory("intropy-scheduling-").FullName;

    private string AppHostDir => Path.Combine(_workspace, "system-host");

    public SchedulingApplyTests()
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

    private static SystemTopology Topology(string? schedule)
    {
        var s = SystemBuilder.Create("order-flow");
        var extractor = s.AddExtractor("order-extractor").Publishes(s_raw);
        if (schedule is not null)
        {
            extractor.WithSchedule(schedule);
        }

        s.AddLoader("order-loader").Subscribes(s_raw);
        return s.Build();
    }

    [Fact]
    public void Apply_WithScheduledComponent_ShouldRegisterTheScheduler()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        IntropyAspire.Apply(builder, Topology("*/5 * * * *"), GeneratedRoot);

        // Assert
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(ComponentScheduler));
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IHostedService));
        var scheduled = Assert.IsAssignableFrom<IReadOnlyList<ScheduledComponent>>(
            builder.Services.Single(d => d.ServiceType == typeof(IReadOnlyList<ScheduledComponent>))
                .ImplementationInstance);
        Assert.Equal([new ScheduledComponent("order-extractor", "*/5 * * * *")], scheduled);
    }

    [Fact]
    public void Apply_ShouldRegisterDaprSidecarRecovery_ForUnscheduledComponents()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        IntropyAspire.Apply(builder, Topology(schedule: null), GeneratedRoot);

        // Assert — startup recovery protects every sidecar, not just cron-triggered components.
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(DaprSidecarRecovery));
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IResourceStateMonitor));
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IBackendReadiness));
    }

    [Fact]
    public void Apply_WithoutScheduledComponents_ShouldNotRegisterTheScheduler()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        IntropyAspire.Apply(builder, Topology(schedule: null), GeneratedRoot);

        // Assert
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(ComponentScheduler));
    }

    [Fact]
    public void Apply_ShouldAddRunNowCommand_OnlyToScheduledComponents()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        IntropyAspire.Apply(builder, Topology("*/5 * * * *"), GeneratedRoot);

        // Assert
        var extractor = builder.Resources.Single(r => r.Name == "order-extractor");
        Assert.Contains(
            extractor.Annotations.OfType<ResourceCommandAnnotation>(), c => c.Name == "intropy-run-now");
        var loader = builder.Resources.Single(r => r.Name == "order-loader");
        Assert.DoesNotContain(
            loader.Annotations.OfType<ResourceCommandAnnotation>(), c => c.Name == "intropy-run-now");
    }

    [Fact]
    public void Apply_ShouldNotAddHttpEndpoint_ToScheduledComponents()
    {
        // Arrange — a scheduled component is a run-to-completion job: no inbound delivery,
        // no app port. Unscheduled components keep the endpoint (and with it their app-port).
        var builder = CreateBuilder();

        // Act
        IntropyAspire.Apply(builder, Topology("*/5 * * * *"), GeneratedRoot);

        // Assert
        var extractor = builder.Resources.Single(r => r.Name == "order-extractor");
        Assert.Empty(extractor.Annotations.OfType<EndpointAnnotation>());
        var loader = builder.Resources.Single(r => r.Name == "order-loader");
        Assert.Single(loader.Annotations.OfType<EndpointAnnotation>());
    }
}

public class ComponentSchedulerTests
{
    private sealed class FakeResourceLifecycle : IResourceLifecycle
    {
        public List<(string Resource, string Command)> Commands { get; } = [];

        /// <summary>Resources reported as not startable (a run still in progress).</summary>
        public HashSet<string> Running { get; } = new(StringComparer.Ordinal);

        /// <summary>Resources whose Start command fails, forcing the Restart fallback.</summary>
        public HashSet<string> FailStart { get; } = new(StringComparer.Ordinal);

        public Task WatchAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public bool IsStartable(string resourceName) => !Running.Contains(resourceName);

        public Task<bool> ExecuteAsync(string resourceName, string commandName, CancellationToken cancellationToken)
        {
            Commands.Add((resourceName, commandName));
            return Task.FromResult(
                commandName != KnownResourceCommands.StartCommand || !FailStart.Contains(resourceName));
        }

        public Task<bool> WaitUntilRunningAsync(
            string resourceName, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private static ComponentScheduler NewScheduler(
        FakeResourceLifecycle lifecycle, TimeProvider? time = null, params ScheduledComponent[] components) =>
        new(components, lifecycle, time ?? TimeProvider.System, NullLogger<ComponentScheduler>.Instance);

    [Fact]
    public async Task TriggerAsync_WhenComponentIsTerminal_ShouldStartSidecarThenApp()
    {
        // Arrange
        var lifecycle = new FakeResourceLifecycle();
        var scheduler = NewScheduler(lifecycle);

        // Act
        await scheduler.TriggerAsync("order-extractor", "cron tick", CancellationToken.None);

        // Assert — the sidecar is brought back before the app.
        Assert.Equal(
            [
                ("order-extractor-dapr-cli", KnownResourceCommands.StartCommand),
                ("order-extractor", KnownResourceCommands.StartCommand),
            ],
            lifecycle.Commands);
    }

    [Fact]
    public async Task TriggerAsync_WhenPreviousRunStillInProgress_ShouldSkipWithoutCommands()
    {
        // Arrange — concurrencyPolicy: Forbid.
        var lifecycle = new FakeResourceLifecycle();
        lifecycle.Running.Add("order-extractor");
        var scheduler = NewScheduler(lifecycle);

        // Act
        await scheduler.TriggerAsync("order-extractor", "cron tick", CancellationToken.None);

        // Assert
        Assert.Empty(lifecycle.Commands);
    }

    [Fact]
    public async Task TriggerAsync_WhenSidecarStillRunning_ShouldStartOnlyTheApp()
    {
        // Arrange — a block that does not shut its sidecar down leaves it Running.
        var lifecycle = new FakeResourceLifecycle();
        lifecycle.Running.Add("order-extractor-dapr-cli");
        var scheduler = NewScheduler(lifecycle);

        // Act
        await scheduler.TriggerAsync("order-extractor", "manual run", CancellationToken.None);

        // Assert
        Assert.Equal([("order-extractor", KnownResourceCommands.StartCommand)], lifecycle.Commands);
    }

    [Fact]
    public async Task TriggerAsync_WhenStartFails_ShouldFallBackToRestart()
    {
        // Arrange — absorbs state races between the scheduler's snapshot and DCP's.
        var lifecycle = new FakeResourceLifecycle();
        lifecycle.FailStart.Add("order-extractor-dapr-cli");
        var scheduler = NewScheduler(lifecycle);

        // Act
        await scheduler.TriggerAsync("order-extractor", "cron tick", CancellationToken.None);

        // Assert
        Assert.Equal(
            [
                ("order-extractor-dapr-cli", KnownResourceCommands.StartCommand),
                ("order-extractor-dapr-cli", KnownResourceCommands.RestartCommand),
                ("order-extractor", KnownResourceCommands.StartCommand),
            ],
            lifecycle.Commands);
    }

    [Fact]
    public async Task Scheduler_ShouldNotCommandAnythingBeforeTheFirstTick_ThenTriggerOnIt()
    {
        // Arrange — the startup run belongs to normal resource auto-start, not the scheduler.
        var lifecycle = new FakeResourceLifecycle();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 30, TimeSpan.Zero));
        var scheduler = NewScheduler(lifecycle, time, new ScheduledComponent("order-extractor", "* * * * *"));

        await ((IHostedService)scheduler).StartAsync(CancellationToken.None);
        try
        {
            // Give the loop a moment to register its delay with the fake clock, then check
            // nothing was commanded before the tick.
            await Task.Delay(50);
            Assert.Empty(lifecycle.Commands);

            // Act — advance past the next whole minute; poll because the tick continuation
            // runs on the thread pool.
            for (var i = 0; i < 100 && lifecycle.Commands.Count < 2; i++)
            {
                time.Advance(TimeSpan.FromSeconds(2));
                await Task.Delay(10);
            }

            // Assert — sidecar first, then the app.
            Assert.True(lifecycle.Commands.Count >= 2, "the cron tick never triggered the component");
            Assert.Equal(("order-extractor-dapr-cli", KnownResourceCommands.StartCommand), lifecycle.Commands[0]);
            Assert.Equal(("order-extractor", KnownResourceCommands.StartCommand), lifecycle.Commands[1]);
        }
        finally
        {
            await ((IHostedService)scheduler).StopAsync(CancellationToken.None);
        }
    }
}
