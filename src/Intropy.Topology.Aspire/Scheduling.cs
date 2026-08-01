using System.Collections.Concurrent;
using Aspire.Hosting.ApplicationModel;
using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Intropy.Topology.Aspire;

/// <summary>A component the scheduler re-runs: its resource name and cron expression.</summary>
internal sealed record ScheduledComponent(string Name, string Cron, IReadOnlyList<string>? Uses = null);

/// <summary>
/// The scheduler's view of the Aspire resource model: last known states plus start/wait
/// commands. A seam so tick behavior is unit-testable without DCP.
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
            // unknown — the caller decides whether to fall back or give up on this tick.
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

/// <summary>
/// Re-runs scheduled components on their cron ticks — the run backend's CronJob emulation.
/// Every component also runs once at host start via normal resource auto-start; this
/// service only owns the re-runs. Ticks fire in local time (the developer's wall clock,
/// unlike Kubernetes' UTC default), a tick that lands while the previous run is still in
/// progress is skipped with a warning (concurrencyPolicy: Forbid), and the sidecar is
/// started before the app because a run-to-completion block shuts its sidecar down when
/// it exits.
/// </summary>
internal sealed class ComponentScheduler(
    IReadOnlyList<ScheduledComponent> components,
    IResourceLifecycle lifecycle,
    TimeProvider timeProvider,
    ILogger<ComponentScheduler> logger,
    MockReadiness? mockReadiness = null) : BackgroundService
{
    private static readonly TimeSpan s_sidecarReadyTimeout = TimeSpan.FromMinutes(3);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new List<Task> { WatchQuietlyAsync(stoppingToken) };
        loops.AddRange(components.Select(component => RunScheduleLoopAsync(component, stoppingToken)));
        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the component's sidecar + app resource pair, unless the previous run is still
    /// in progress (skipped with a warning). Shared by cron ticks and the dashboard's
    /// "Run now" command.
    /// </summary>
    internal async Task TriggerAsync(string component, string reason, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(component, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning(
                "intropy-schedule: skipping {Reason} for {Component}: a trigger is already executing",
                reason, component);
            return;
        }

        try
        {
            if (mockReadiness is not null)
            {
                var uses = components.SingleOrDefault(candidate => candidate.Name == component)?.Uses ?? [];
                await mockReadiness.WaitForAsync(uses, TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
            }

            if (!lifecycle.IsStartable(component))
            {
                logger.LogWarning(
                    "intropy-schedule: skipping {Reason} for {Component}: the previous run is still in progress",
                    reason, component);
                return;
            }

            // The run-to-completion block shut its sidecar down when it exited, so bring
            // the sidecar back first — daprd must be accepting connections before the app
            // starts, or the app's first sidecar call fails.
            var sidecar = DaprSidecarIdentity.CliName(component);
            if (lifecycle.IsStartable(sidecar))
            {
                if (!await StartResourceAsync(sidecar, cancellationToken).ConfigureAwait(false))
                {
                    logger.LogError(
                        "intropy-schedule: could not start sidecar {Sidecar}; abandoning {Reason} for {Component}",
                        sidecar, reason, component);
                    return;
                }

                if (!await lifecycle.WaitUntilRunningAsync(sidecar, s_sidecarReadyTimeout, cancellationToken)
                        .ConfigureAwait(false))
                {
                    logger.LogError(
                        "intropy-schedule: sidecar {Sidecar} did not reach Running within {Timeout}; " +
                        "abandoning {Reason} for {Component}",
                        sidecar, s_sidecarReadyTimeout, reason, component);
                    return;
                }
            }

            if (!await StartResourceAsync(component, cancellationToken).ConfigureAwait(false))
            {
                logger.LogError("intropy-schedule: could not start {Component} ({Reason})", component, reason);
                return;
            }

            logger.LogInformation("intropy-schedule: triggered {Component} ({Reason})", component, reason);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> StartResourceAsync(string resourceName, CancellationToken cancellationToken)
    {
        // Start is right for a terminal/never-started resource; fall back to Restart to
        // absorb state races between our snapshot and DCP's.
        return await lifecycle.ExecuteAsync(resourceName, KnownResourceCommands.StartCommand, cancellationToken)
                   .ConfigureAwait(false)
               || await lifecycle.ExecuteAsync(resourceName, KnownResourceCommands.RestartCommand, cancellationToken)
                   .ConfigureAwait(false);
    }

    private async Task RunScheduleLoopAsync(ScheduledComponent component, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = timeProvider.GetUtcNow();
                var next = CronExpression.Parse(component.Cron).GetNextOccurrence(now, TimeZoneInfo.Local);
                if (next is null)
                {
                    logger.LogWarning(
                        "intropy-schedule: {Component}'s schedule '{Cron}' never fires again; stopping its loop",
                        component.Name, component.Cron);
                    return;
                }

                logger.LogInformation(
                    "intropy-schedule: {Component} next run at {Next:o} ({Cron})",
                    component.Name, next.Value.ToLocalTime(), component.Cron);

                var delay = next.Value - now;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
                }

                await TriggerAsync(component.Name, "cron tick", stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    private async Task WatchQuietlyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await lifecycle.WatchAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }
}
