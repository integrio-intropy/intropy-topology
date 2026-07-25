using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace Intropy.Topology.Aspire.Test;

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
        FakeLifecycle lifecycle, FakeBackendReadiness backendReadiness) =>
        new(new FakeStateMonitor(), lifecycle, backendReadiness, NullLogger<DaprSidecarRecovery>.Instance);

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
        var recovery = NewRecovery(lifecycle, backendReadiness);

        // Act
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.Running),
            CancellationToken.None);
        await recovery.HandleStateUpdateAsync(
            new ResourceStateUpdate("order-extractor-dapr-cli", KnownResourceStates.Finished),
            CancellationToken.None);

        // Assert
        Assert.Equal(0, backendReadiness.WaitCalls);
        Assert.Empty(lifecycle.Commands);
    }
}
