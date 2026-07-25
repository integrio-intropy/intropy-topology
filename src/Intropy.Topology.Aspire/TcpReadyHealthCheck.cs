using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Intropy.Topology.Aspire;

internal interface IBackendReadiness
{
    Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Readiness probe for a published container port. A bare TCP connect is not enough: both the
/// Docker Desktop port proxy and Aspire's DCP endpoint proxy accept connections before the
/// backend listens — DCP holds them open in silence, so even "connection survives a read
/// window" passes against a proxy whose backend is down. When the probed protocol lets the
/// client speak first, pass its opening bytes as <paramref name="handshake"/>: readiness then
/// requires an actual response, which only the real backend can produce. Without a handshake,
/// silence within the read window still counts as ready (a listener with no banner).
/// </summary>
internal sealed class TcpReadyHealthCheck(string host, int port, ReadOnlyMemory<byte> handshake = default)
    : IHealthCheck, IBackendReadiness
{
    private static readonly TimeSpan s_readWindow = TimeSpan.FromMilliseconds(750);

    /// <summary>The inline RESP command "PING\r\n"; a ready Redis answers it with +PONG.</summary>
    public static ReadOnlyMemory<byte> RedisHandshake { get; } = "PING\r\n"u8.ToArray();

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
            var stream = client.GetStream();
            if (!handshake.IsEmpty)
            {
                await stream.WriteAsync(handshake, cancellationToken).ConfigureAwait(false);
            }

            using var readWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readWindow.CancelAfter(s_readWindow);
            try
            {
                var read = await stream.ReadAsync(new byte[1], readWindow.Token).ConfigureAwait(false);
                return read > 0
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy($"{host}:{port} closed the connection immediately.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // No bytes within the window but the connection stayed open. A real listener may
                // simply have no banner; a proxy dialing a dead backend looks exactly the same,
                // so with a handshake in flight only an actual response counts.
                return handshake.IsEmpty
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy($"{host}:{port} did not answer the handshake.");
            }
        }
        catch (SocketException ex)
        {
            return HealthCheckResult.Unhealthy($"{host}:{port} not reachable: {ex.Message}");
        }
        catch (IOException ex)
        {
            return HealthCheckResult.Unhealthy($"{host}:{port} dropped the connection: {ex.Message}");
        }
    }
}
