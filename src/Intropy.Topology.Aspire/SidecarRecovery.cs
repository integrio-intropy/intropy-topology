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
/// Restarts Dapr sidecars that exit while initializing a component. Component initialization is
/// fatal in daprd, and a broker can accept TCP connections shortly before it is ready to accept
/// the component connection. Initial startup is gated separately; this service handles the
/// remaining transient window without requiring dashboard intervention.
/// </summary>
internal sealed class BackendReadiness(IReadOnlyList<IBackendReadiness> backends) : IBackendReadiness
{
    public async Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        foreach (var backend in backends)
        {
            await backend.WaitUntilReadyAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Recovery configuration: which sidecars the scheduler owns and recovery must skip.</summary>
internal sealed record DaprSidecarRecoveryOptions(IReadOnlySet<string> SchedulerOwnedSidecars);

internal sealed class DaprSidecarRecovery(
    IResourceStateMonitor states,
    IResourceLifecycle lifecycle,
    IBackendReadiness backendReadiness,
    DaprSidecarRecoveryOptions options,
    TimeProvider time,
    ILogger<DaprSidecarRecovery> logger) : BackgroundService
{
    private const int MaxAttemptsPerSidecar = 3;
    private static readonly TimeSpan s_backendReadyTimeout = TimeSpan.FromMinutes(3);

    // DCP reports an executable as Running the moment its process launches, not when it is
    // healthy — daprd lives ~1s before a fatal component-init exit. Only a stay in Running at
    // least this long counts as having actually run; a scheduled sidecar that legitimately
    // completes faster costs at most MaxAttemptsPerSidecar spurious restarts.
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
        if (!update.ResourceName.EndsWith("-dapr-cli", StringComparison.Ordinal))
        {
            return;
        }

        // A scheduled component's sidecar is scheduler-owned: the run-to-completion block shuts
        // its sidecar down after every run and the scheduler restarts it on the next tick
        // (decision 0010), so its exits are lifecycle, not failure — however quick the run.
        if (options.SchedulerOwnedSidecars.Contains(update.ResourceName))
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

        // A normal scheduled sidecar exits after its app completes — but only a sustained stay
        // in Running proves that, so a component-init crash a moment after launch still recovers.
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
