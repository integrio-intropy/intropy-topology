using System.Collections.Concurrent;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Intropy.Topology.Aspire;

/// <summary>The names of the run-to-completion components (their project resources).</summary>
internal sealed record RunToCompletionComponents(IReadOnlySet<string> Names);

/// <summary>
/// Re-runs run-to-completion components on a fixed interval so file-driven integrations keep
/// sweeping during local development. The framework's <c>JobRunner</c> exits the project and
/// shuts the Dapr sidecar down together, so a terminal state on the project proves both are
/// done; both are then started again, in no particular order — <c>JobRunner</c> waits for its
/// own sidecar before executing.
/// </summary>
internal sealed class RunToCompletionScheduler(
    IResourceStateMonitor states,
    IResourceLifecycle lifecycle,
    RunToCompletionComponents runToCompletion,
    TimeProvider time,
    ILogger<RunToCompletionScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan s_restartDelay = TimeSpan.FromMinutes(1);

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
        // The set holds project names, so sidecar updates ("...-dapr-cli") never schedule
        // here: a run's sidecar exits with or before its project, so only the project has a
        // terminal state left to observe.
        if (!runToCompletion.Names.Contains(update.ResourceName))
        {
            return;
        }

        // Success (exit code 0) and job failure (1) both end in Finished/Exited and both
        // schedule the next run: failed files stay on the source port and the next sweep
        // retries them, which the components' idempotency makes safe. FailedToStart is not a
        // run outcome — it schedules nothing.
        var completed = string.Equals(update.State, KnownResourceStates.Finished, StringComparison.Ordinal)
            || string.Equals(update.State, KnownResourceStates.Exited, StringComparison.Ordinal);
        if (!completed)
        {
            return;
        }

        // Only one pending restart per component: duplicate terminal reports during the delay
        // window (a pair can be re-reported while waiting) must not pile up extra runs.
        var gate = _gates.GetOrAdd(update.ResourceName, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            logger.LogInformation(
                "intropy-scheduler: {Component} completed a run; scheduling its next run for {ScheduledAt:u}",
                update.ResourceName, time.GetLocalNow() + s_restartDelay);

            await Task.Delay(s_restartDelay, time, cancellationToken).ConfigureAwait(false);
            await StartPairAsync(update.ResourceName, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "intropy-scheduler: unable to schedule the next run of {Component}", update.ResourceName);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task StartPairAsync(string componentName, CancellationToken cancellationToken)
    {
        // A sweep still in progress when the delay elapses must not be restarted; its own
        // terminal state will schedule its next run. The project check is enough — the sidecar
        // cannot outlive it, and the project never reaches Running without it.
        if (!lifecycle.IsStartable(componentName))
        {
            logger.LogInformation(
                "intropy-scheduler: {Component} is still running; deferring its next run", componentName);
            return;
        }

        await StartAsync(componentName, cancellationToken).ConfigureAwait(false);
        await StartAsync(DaprSidecarIdentity.CliName(componentName), cancellationToken).ConfigureAwait(false);
    }

    private async Task StartAsync(string resourceName, CancellationToken cancellationToken)
    {
        // Start is the command for a resource in a startable terminal state; Restart is the
        // state-race fallback when the state has moved on since it was observed.
        var started = await lifecycle
            .ExecuteAsync(resourceName, KnownResourceCommands.StartCommand, cancellationToken)
            .ConfigureAwait(false)
            || await lifecycle
                .ExecuteAsync(resourceName, KnownResourceCommands.RestartCommand, cancellationToken)
                .ConfigureAwait(false);
        if (!started)
        {
            logger.LogWarning(
                "intropy-scheduler: start command for {Resource} was unavailable or failed", resourceName);
        }
    }
}
