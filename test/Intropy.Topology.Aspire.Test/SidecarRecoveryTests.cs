using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Intropy.Topology.Aspire.Test;

public sealed class DaprSidecarIdentityTests
{
    [Fact]
    public void CliName_ShouldMatchThePinnedToolkitExecutableConvention()
    {
        // CommunityToolkit.Aspire.Hosting.Dapr 13.0.0 materializes this executable name.
        Assert.Equal("order-extractor-dapr-cli", DaprSidecarIdentity.CliName("order-extractor"));
        Assert.True(DaprSidecarIdentity.IsCli("order-extractor-dapr-cli"));
        Assert.False(DaprSidecarIdentity.IsCli("order-extractor-dapr"));
    }
}

public sealed class DaprSidecarRecoveryTests
{
    private sealed class FakeStateMonitor : IResourceStateMonitor
    {
        public async IAsyncEnumerable<ResourceStateUpdate> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class FakeLifecycle : IResourceLifecycle
    {
        public List<(string Resource, string Command)> Commands { get; } = [];
        public bool RestartSucceeds { get; set; } = true;

        public Task WatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool IsStartable(string resourceName) => true;

        public Task<bool> ExecuteAsync(string resourceName, string commandName, CancellationToken cancellationToken)
        {
            Commands.Add((resourceName, commandName));
            return Task.FromResult(commandName != KnownResourceCommands.RestartCommand || RestartSucceeds);
        }

        public Task<bool> WaitUntilRunningAsync(
            string resourceName, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FakeBackendReadiness : IBackendReadiness
    {
        public int WaitCalls { get; private set; }

        public Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            WaitCalls++;
            return Task.CompletedTask;
        }
    }

    private static DaprSidecarRecovery NewRecovery(
        FakeLifecycle lifecycle, FakeBackendReadiness backendReadiness, TimeProvider? time = null,
        params string[] schedulerOwnedSidecars) =>
        new(new FakeStateMonitor(), lifecycle, backendReadiness,
            new DaprSidecarRecoveryOptions(schedulerOwnedSidecars.ToHashSet(StringComparer.Ordinal)),
            time ?? TimeProvider.System, NullLogger<DaprSidecarRecovery>.Instance);

    [Fact]
    public async Task HandleStateUpdateAsync_WhenDaprSidecarFails_ShouldWaitForBackendThenRestart()
    {
        // Arrange
        var lifecycle = new FakeLifecycle();
        var backendReadiness = new FakeBackendReadiness();
        var recovery = NewRecovery(lifecycle, backendReadiness);

        // Act
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.FailedToStart),
            CancellationToken.None);

        // Assert
        Assert.Equal(1, backendReadiness.WaitCalls);
        Assert.Equal(
            [("order-extractor-dapr-cli", KnownResourceCommands.RestartCommand)],
            lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenSidecarFinishesBeforeRunning_ShouldRestartIt()
    {
        // Arrange — daprd exits when component initialization fails after the process launches.
        var lifecycle = new FakeLifecycle();
        var backendReadiness = new FakeBackendReadiness();
        var recovery = NewRecovery(lifecycle, backendReadiness);

        // Act
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.Finished),
            CancellationToken.None);

        // Assert
        Assert.Equal(1, backendReadiness.WaitCalls);
        Assert.Equal(
            [("order-extractor-dapr-cli", KnownResourceCommands.RestartCommand)],
            lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenRestartIsUnavailable_ShouldFallBackToStart()
    {
        // Arrange
        var lifecycle = new FakeLifecycle { RestartSucceeds = false };
        var backendReadiness = new FakeBackendReadiness();
        var recovery = NewRecovery(lifecycle, backendReadiness);

        // Act
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.FailedToStart),
            CancellationToken.None);

        // Assert
        Assert.Equal(
            [
                ("order-extractor-dapr-cli", KnownResourceCommands.RestartCommand),
                ("order-extractor-dapr-cli", KnownResourceCommands.StartCommand),
            ],
            lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenSchedulerOwnedSidecarExitsQuickly_ShouldIgnoreIt()
    {
        // Arrange — a run-to-completion block shuts its sidecar down after every run, often
        // well inside any healthy-duration window; the scheduler restarts it on the next tick.
        var lifecycle = new FakeLifecycle();
        var backendReadiness = new FakeBackendReadiness();
        var time = new FakeTimeProvider();
        var recovery = NewRecovery(lifecycle, backendReadiness, time, "order-extractor-dapr-cli");

        // Act
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.Running),
            CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.Finished),
            CancellationToken.None);

        // Assert
        Assert.Equal(0, backendReadiness.WaitCalls);
        Assert.Empty(lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenNonSidecarFails_ShouldIgnoreIt()
    {
        // Arrange
        var lifecycle = new FakeLifecycle();
        var backendReadiness = new FakeBackendReadiness();
        var recovery = NewRecovery(lifecycle, backendReadiness);

        // Act
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor", KnownResourceStates.FailedToStart),
            CancellationToken.None);

        // Assert
        Assert.Equal(0, backendReadiness.WaitCalls);
        Assert.Empty(lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenRecoveryKeepsFailing_ShouldStopAfterThreeAttempts()
    {
        // Arrange
        var lifecycle = new FakeLifecycle();
        var backendReadiness = new FakeBackendReadiness();
        var recovery = NewRecovery(lifecycle, backendReadiness);
        var failure = new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.FailedToStart);

        // Act
        for (var i = 0; i < 4; i++)
        {
            await recovery.HandleStateUpdateAsync(failure, CancellationToken.None);
        }

        // Assert
        Assert.Equal(3, backendReadiness.WaitCalls);
        Assert.Equal(3, lifecycle.Commands.Count);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenSidecarRanThenFinished_ShouldNotRestartIt()
    {
        // Arrange — scheduled sidecars finish normally after their run-to-completion app exits.
        var lifecycle = new FakeLifecycle();
        var backendReadiness = new FakeBackendReadiness();
        var time = new FakeTimeProvider();
        var recovery = NewRecovery(lifecycle, backendReadiness, time);

        // Act
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.Running),
            CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(30));
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.Finished),
            CancellationToken.None);

        // Assert
        Assert.Equal(0, backendReadiness.WaitCalls);
        Assert.Empty(lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenSidecarCrashesShortlyAfterRunning_ShouldRestartIt()
    {
        // Arrange — DCP marks an executable Running at process launch; daprd's fatal
        // component-init exit follows about a second later and must still be recovered.
        var lifecycle = new FakeLifecycle();
        var backendReadiness = new FakeBackendReadiness();
        var time = new FakeTimeProvider();
        var recovery = NewRecovery(lifecycle, backendReadiness, time);

        // Act
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-loader-dapr-cli", KnownResourceStates.Running),
            CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-loader-dapr-cli", KnownResourceStates.Finished),
            CancellationToken.None);

        // Assert
        Assert.Equal(1, backendReadiness.WaitCalls);
        Assert.Equal(
            [("order-loader-dapr-cli", KnownResourceCommands.RestartCommand)],
            lifecycle.Commands);
    }

    [Fact]
    public async Task HandleStateUpdateAsync_WhenRestartedSidecarRunsHealthily_ShouldNotRestartItAgain()
    {
        // Arrange — the Running stamp must reset per instance: a healthy stretch after recovery
        // makes the eventual normal exit a non-event.
        var lifecycle = new FakeLifecycle();
        var backendReadiness = new FakeBackendReadiness();
        var time = new FakeTimeProvider();
        var recovery = NewRecovery(lifecycle, backendReadiness, time);
        var sidecar = "order-loader-dapr-cli";

        // Act — crash fast, recover, then run healthily and finish.
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate(sidecar, KnownResourceStates.Running), CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate(sidecar, KnownResourceStates.Finished), CancellationToken.None);
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate(sidecar, KnownResourceStates.Running), CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(30));
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate(sidecar, KnownResourceStates.Finished), CancellationToken.None);

        // Assert — exactly the one recovery from the crash, nothing for the healthy exit.
        Assert.Equal([(sidecar, KnownResourceCommands.RestartCommand)], lifecycle.Commands);
    }
}
