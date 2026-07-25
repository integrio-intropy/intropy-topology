using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Intropy.Topology.Aspire;

/// <summary>
/// Readiness probe for a published container port. A bare TCP connect is not enough on Docker
/// Desktop — the port proxy accepts connections before the process inside the container listens,
/// then closes them — so the probe connects and requires the connection to survive a short read
/// window (a server banner or plain silence both pass); an immediate EOF means not ready.
/// </summary>
internal sealed class TcpReadyHealthCheck(string host, int port) : IHealthCheck
{
    private static readonly TimeSpan s_readWindow = TimeSpan.FromMilliseconds(750);

    /// <summary>Polls until the port is ready, throwing if it never becomes so.</summary>
    public async Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var result = await CheckHealthAsync(new HealthCheckContext(), deadline.Token).ConfigureAwait(false);
                if (result.Status == HealthStatus.Healthy)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"{host}:{port} did not become ready within {timeout}.");
        }
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

            using var readWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readWindow.CancelAfter(s_readWindow);
            try
            {
                var read = await client.GetStream().ReadAsync(new byte[1], readWindow.Token).ConfigureAwait(false);
                return read > 0
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy($"{host}:{port} closed the connection immediately.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // No banner within the window but the connection stayed open: a real listener.
                return HealthCheckResult.Healthy();
            }
        }
        catch (SocketException ex)
        {
            return HealthCheckResult.Unhealthy($"{host}:{port} not reachable: {ex.Message}");
        }
    }
}
