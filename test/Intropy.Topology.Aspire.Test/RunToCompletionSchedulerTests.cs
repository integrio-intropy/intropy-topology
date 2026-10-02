using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Intropy.Topology.Aspire.Test;

public sealed class RunToCompletionSchedulerTests
{
    private sealed class FakeStateMonitor : IResourceStateMonitor
    {
        public IAsyncEnumerable<ResourceStateUpdate> WatchAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Scheduling is exercised through HandleStateUpdateAsync.");
        }
    }

    private sealed class FakeLifecycle : IResourceLifecycle
    {
        public List<(string Resource, string Command)> Commands { get; } = [];
        public HashSet<string> NotStartable { get; } = new(StringComparer.Ordinal);
        public bool CommandsFail { get; set; }

        public Task WatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool IsStartable(string resourceName) => !NotStartable.Contains(resourceName);

        public Task<bool> ExecuteAsync(string resourceName, string commandName, CancellationToken cancellationToken)
        {
            Commands.Add((resourceName, commandName));
            return Task.FromResult(!CommandsFail);
        }

        public Task<bool> WaitUntilRunningAsync(
            string resourceName, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class RecordingLogger : ILogger<RunToCompletionScheduler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static readonly RunToCompletionComponents s_oneExtractor =
        new(new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            ["order-extractor"] = RunToCompletionComponents.DefaultRestartDelay,
        });

    private static RunToCompletionScheduler NewScheduler(
        FakeLifecycle lifecycle, TimeProvider time) =>
        new(new FakeStateMonitor(), lifecycle, s_oneExtractor, time, new RecordingLogger());

    [Fact]
    public async Task HandleStateUpdateAsync_WhenProjectFinishes_ShouldStartBothResourcesAfterOneMinute()
    {
        // Arrange
        var lifecycle = new FakeLifecycle();
        var time = new FakeTimeProvider();
        var scheduler = NewScheduler(lifecycle, time);

        // Act — the handler parks on the delay until the fake clock moves; nothing starts before.
        var task = scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.Finished), CancellationToken.None);
        Assert.Empty(lifecycle.Commands);
        time.Advance(TimeSpan.FromMinutes(1));
        await task;

        // Assert — no ordering is imposed between the pair; the framework's JobRunner waits
        // for its own sidecar.
        Assert.Equal(
            [
                ("order-extractor", KnownResourceCommands.StartCommand),
                ("order-extractor-dapr-cli", KnownResourceCommands.StartCommand),
            ],
            lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WithDeclaredCadence_ShouldRestartAfterThatDelay()
    {
        // Arrange — the development manifest declared a ten-second cadence for this component.
        var lifecycle = new FakeLifecycle();
        var time = new FakeTimeProvider();
        var scheduler = new RunToCompletionScheduler(
            new FakeStateMonitor(), lifecycle,
            new RunToCompletionComponents(new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
            {
                ["order-extractor"] = TimeSpan.FromSeconds(10),
            }),
            time, new RecordingLogger());

        // Act
        var task = scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.Finished), CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.Empty(lifecycle.Commands);
        time.Advance(TimeSpan.FromSeconds(1));
        await task;

        // Assert
        Assert.Equal(2, lifecycle.Commands.Count);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenProjectExits_ShouldScheduleFromExitedToo()
    {
        // Arrange
        var lifecycle = new FakeLifecycle();
        var time = new FakeTimeProvider();
        var scheduler = NewScheduler(lifecycle, time);

        // Act
        var task = scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.Exited), CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        await task;

        // Assert
        Assert.Equal(2, lifecycle.Commands.Count);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenComponentIsNotRunToCompletion_ShouldIgnoreIt()
    {
        // Arrange
        var lifecycle = new FakeLifecycle();
        var time = new FakeTimeProvider();
        var scheduler = NewScheduler(lifecycle, time);

        // Act — a loader is resident; its states never schedule anything. No internal delay
        // means the call returns without any clock movement.
        await scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-loader", KnownResourceStates.Finished), CancellationToken.None);

        // Assert
        Assert.Empty(lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenSidecarReports_ShouldIgnoreIt()
    {
        // Arrange — only the project resource schedules; the sidecar's terminal state must not
        // trigger its own restart cycle.
        var lifecycle = new FakeLifecycle();
        var time = new FakeTimeProvider();
        var scheduler = NewScheduler(lifecycle, time);

        // Act
        await scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.Exited), CancellationToken.None);

        // Assert
        Assert.Empty(lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenComponentIsStillRunning_ShouldNotRestartIt()
    {
        // Arrange — a sweep longer than a minute must never overlap itself.
        var lifecycle = new FakeLifecycle();
        lifecycle.NotStartable.Add("order-extractor");
        var time = new FakeTimeProvider();
        var scheduler = NewScheduler(lifecycle, time);

        // Act
        var task = scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.Finished), CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        await task;

        // Assert
        Assert.Empty(lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenDuplicateTerminalReportsArrive_ShouldScheduleOnlyOneRestart()
    {
        // Arrange
        var lifecycle = new FakeLifecycle();
        var time = new FakeTimeProvider();
        var scheduler = NewScheduler(lifecycle, time);

        // Act — DCP can re-report a pair's terminal state while the first run is still waiting.
        var first = scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.Finished), CancellationToken.None);
        var duplicate = scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.Exited), CancellationToken.None);
        await duplicate;
        time.Advance(TimeSpan.FromMinutes(1));
        await first;

        // Assert
        Assert.Equal(2, lifecycle.Commands.Count);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenCommandsFail_ShouldLogWarningsAndKeepScheduling()
    {
        // Arrange
        var logger = new RecordingLogger();
        var lifecycle = new FakeLifecycle { CommandsFail = true };
        var time = new FakeTimeProvider();
        var scheduler = new RunToCompletionScheduler(
            new FakeStateMonitor(), lifecycle, s_oneExtractor, time, logger);

        // Act
        var task = scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.Finished), CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        await task;

        // Assert — the failure is surfaced, and a next terminal state still schedules and
        // completes (the service did not crash on the failed commands).
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(4, lifecycle.Commands.Count);
        var next = scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.Finished), CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        await next;
        Assert.Equal(8, lifecycle.Commands.Count);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenComponentFailsToStart_ShouldNotSchedule()
    {
        // Arrange — FailedToStart is not a run outcome and never schedules a restart.
        var lifecycle = new FakeLifecycle();
        var time = new FakeTimeProvider();
        var scheduler = NewScheduler(lifecycle, time);

        // Act
        await scheduler.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.FailedToStart), CancellationToken.None);

        // Assert
        Assert.Empty(lifecycle.Commands);
    }
}
