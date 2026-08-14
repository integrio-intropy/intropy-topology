using System.Collections.Concurrent;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Intropy.Topology.Aspire;

/// <summary>A resource-state update consumed by sidecar recovery.</summary>
internal sealed record ResourceStateUpdate(string ResourceName, string? State);

/// <summary>Streams Aspire resource state changes without exposing Aspire's event types.</summary>
internal interface IResourceStateMonitor
{
    IAsyncEnumerable<ResourceStateUpdate> WatchAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The recovery service's view of the Aspire resource model: last known states plus start/wait
/// commands. A seam so behavior is unit-testable without DCP.
/// </summary>
internal interface IResourceLifecycle
{
    /// <summary>Maintains the last-known-state map; runs for the host's lifetime.</summary>
    Task WatchAsync(CancellationToken cancellationToken);

    /// <summary>Whether the resource can be started: never seen, or in a terminal state.
    /// A non-terminal state means a run is still in progress.</summary>
    bool IsStartable(string resourceName);

    /// <summary>Executes a resource command; false when the command fails or is unavailable.</summary>
    Task<bool> ExecuteAsync(string resourceName, string commandName, CancellationToken cancellationToken);

    /// <summary>Waits for the resource to reach Running; false on timeout.</summary>
    Task<bool> WaitUntilRunningAsync(string resourceName, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Production <see cref="IResourceLifecycle"/> over the Aspire services.</summary>
internal sealed class AspireResourceLifecycle(
    ResourceCommandService commands,
    ResourceNotificationService notifications) : IResourceLifecycle
{
    private readonly ConcurrentDictionary<string, string?> _states = new(StringComparer.Ordinal);

    public async Task WatchAsync(CancellationToken cancellationToken)
    {
        await foreach (var evt in notifications.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            _states[evt.Resource.Name] = evt.Snapshot.State?.Text;
        }
    }

    public bool IsStartable(string resourceName)
    {
        var state = _states.GetValueOrDefault(resourceName);
        return state is null || KnownResourceStates.TerminalStates.Contains(state, StringComparer.Ordinal);
    }

    public async Task<bool> ExecuteAsync(string resourceName, string commandName, CancellationToken cancellationToken)
    {
        try
        {
            var result = await commands
                .ExecuteCommandAsync(resourceName, commandName, cancellationToken)
                .ConfigureAwait(false);
            return result.Success;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Command unavailable in the resource's current state, or the resource is
            // unknown — the caller decides whether to fall back or give up.
            return false;
        }
    }

    public async Task<bool> WaitUntilRunningAsync(
        string resourceName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await notifications
                .WaitForResourceAsync(resourceName, KnownResourceStates.Running, deadline.Token)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

/// <summary>Production resource-state monitor over Aspire notifications.</summary>
internal sealed class AspireResourceStateMonitor(ResourceNotificationService notifications) : IResourceStateMonitor
{
    public async IAsyncEnumerable<ResourceStateUpdate> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in notifications.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new ResourceStateUpdate(update.Resource.Name, update.Snapshot.State?.Text);
        }
    }
}

/// <summary>
/// Compatibility seam for the executable resource created by CommunityToolkit.Aspire.Hosting.Dapr 13.0.0.
/// The toolkit's public sidecar model does not expose this executable, so audit this convention when
/// upgrading the toolkit.
/// </summary>
internal static class DaprSidecarIdentity
{
    private const string CliSuffix = "-dapr-cli";

    public static string CliName(string componentName) => componentName + CliSuffix;

    public static bool IsCli(string resourceName) => resourceName.EndsWith(CliSuffix, StringComparison.Ordinal);
}

/// <summary>
/// The sidecar executables of run-to-completion components. Their components shut the sidecar
/// down deliberately at the end of the run, so a Finished/Exited state is the intended terminal
/// state — recovery must leave them alone. Registered by the host from the topology's kinds.
/// </summary>
internal sealed record RunToCompletionSidecars(IReadOnlySet<string> Names);

internal sealed class DaprSidecarRecovery(
    IResourceStateMonitor states,
    IResourceLifecycle lifecycle,
    IBackendReadiness backendReadiness,
    RunToCompletionSidecars runToCompletion,
    TimeProvider time,
    ILogger<DaprSidecarRecovery> logger) : BackgroundService
{
    private const int MaxAttemptsPerSidecar = 3;
    private static readonly TimeSpan s_backendReadyTimeout = TimeSpan.FromMinutes(3);

    // DCP reports an executable as Running the moment its process launches, not when it is
    // healthy — daprd lives ~1s before a fatal component-init exit. Only a stay in Running at
    // least this long counts as having actually run; a sidecar that legitimately completes
    // faster costs at most MaxAttemptsPerSidecar spurious restarts.
    private static readonly TimeSpan s_minHealthyRunningDuration = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _runningSince = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var update in states.WatchAsync(stoppingToken).ConfigureAwait(false))
            {
                await HandleStateUpdateAsync(update, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    internal async Task HandleStateUpdateAsync(ResourceStateUpdate update, CancellationToken cancellationToken)
    {
        if (!DaprSidecarIdentity.IsCli(update.ResourceName))
        {
            return;
        }

        // A run-to-completion sidecar's exit — however fast — is the component's intended
        // terminal state, decided by the framework runner. No timing heuristic applies.
        if (runToCompletion.Names.Contains(update.ResourceName))
        {
            return;
        }

        if (string.Equals(update.State, KnownResourceStates.Running, StringComparison.Ordinal))
        {
            // Overwrite (not TryAdd): after a recovery restart the fresh instance's clock must
            // start over, or its own early crash would be judged by the previous run's stamp.
            _runningSince[update.ResourceName] = time.GetTimestamp();
            return;
        }

        // Only a sustained stay in Running proves the sidecar initialized; a component-init
        // crash a moment after launch must still recover.
        var ranHealthily = _runningSince.TryGetValue(update.ResourceName, out var since)
            && time.GetElapsedTime(since) >= s_minHealthyRunningDuration;
        var failedBeforeRunning = string.Equals(update.State, KnownResourceStates.Finished, StringComparison.Ordinal)
            || string.Equals(update.State, KnownResourceStates.Exited, StringComparison.Ordinal);
        if (!string.Equals(update.State, KnownResourceStates.FailedToStart, StringComparison.Ordinal)
            && (!failedBeforeRunning || ranHealthily))
        {
            return;
        }

        var gate = _gates.GetOrAdd(update.ResourceName, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var attempt = _attempts.AddOrUpdate(update.ResourceName, 1, (_, prior) => prior + 1);
            if (attempt > MaxAttemptsPerSidecar)
            {
                logger.LogError(
                    "intropy-dapr: {Sidecar} exhausted {MaxAttempts} recovery attempts; inspect its component configuration",
                    update.ResourceName, MaxAttemptsPerSidecar);
                return;
            }

            logger.LogWarning(
                "intropy-dapr: {Sidecar} terminated before completing component initialization; waiting for backends before recovery attempt {Attempt}/{MaxAttempts}",
                update.ResourceName, attempt, MaxAttemptsPerSidecar);
            await backendReadiness.WaitUntilReadyAsync(s_backendReadyTimeout, cancellationToken).ConfigureAwait(false);

            // FailedToStart normally accepts Restart. Start is a state-race fallback when DCP
            // has already moved the resource to a startable terminal state.
            var recovered = await lifecycle
                .ExecuteAsync(update.ResourceName, KnownResourceCommands.RestartCommand, cancellationToken)
                .ConfigureAwait(false)
                || await lifecycle
                    .ExecuteAsync(update.ResourceName, KnownResourceCommands.StartCommand, cancellationToken)
                    .ConfigureAwait(false);
            if (!recovered)
            {
                logger.LogWarning(
                    "intropy-dapr: recovery command for {Sidecar} was unavailable or failed (attempt {Attempt}/{MaxAttempts})",
                    update.ResourceName, attempt, MaxAttemptsPerSidecar);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "intropy-dapr: unable to recover {Sidecar}", update.ResourceName);
        }
        finally
        {
            gate.Release();
        }
    }
}
