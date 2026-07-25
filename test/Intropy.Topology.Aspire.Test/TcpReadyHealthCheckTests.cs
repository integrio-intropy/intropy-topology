using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Intropy.Topology.Aspire.Test;

public sealed class TcpReadyHealthCheckTests
{
    /// <summary>A loopback listener whose per-connection behavior the test scripts.</summary>
    private sealed class ScriptedListener : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _accepting;
        private readonly CancellationTokenSource _stop = new();

        public int Port { get; }

        public ScriptedListener(Func<TcpClient, CancellationToken, Task> onConnection)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accepting = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await onConnection(client, _stop.Token);
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Dispose();
            try
            {
                await _accepting;
            }
            catch (OperationCanceledException)
            {
                // Listener shutdown.
            }
            catch (SocketException)
            {
                // Accept aborted by Stop.
            }

            _stop.Dispose();
        }
    }

    private static Task HoldOpenSilently(TcpClient client, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ContinueWith(
            _ => { }, TaskScheduler.Default);

    [Fact]
    public async Task CheckHealthAsync_WhenListenerAnswersHandshake_ShouldBeHealthy()
    {
        // Arrange — a ready backend answers the client's opening bytes, as Redis does.
        await using var listener = new ScriptedListener(async (client, cancellationToken) =>
        {
            var buffer = new byte[8];
            _ = await client.GetStream().ReadAsync(buffer, cancellationToken);
            await client.GetStream().WriteAsync(new byte[] { 1 }, cancellationToken);
        });
        var probe = new TcpReadyHealthCheck("localhost", listener.Port, TcpReadyHealthCheck.RedisHandshake);

        // Act
        var result = await probe.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenHandshakeGetsOnlySilence_ShouldBeUnhealthy()
    {
        // Arrange — Aspire's DCP endpoint proxy accepts connections from host start and holds
        // them open in silence while its backend is still down; that must not count as ready.
        await using var listener = new ScriptedListener(HoldOpenSilently);
        var probe = new TcpReadyHealthCheck("localhost", listener.Port, TcpReadyHealthCheck.RedisHandshake);

        // Act
        var result = await probe.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WithoutHandshake_ShouldTreatSilentListenerAsHealthy()
    {
        // Arrange — bannerless protocols keep the original semantics: an open socket is enough.
        await using var listener = new ScriptedListener(HoldOpenSilently);
        var probe = new TcpReadyHealthCheck("localhost", listener.Port);

        // Act
        var result = await probe.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenConnectionIsClosedImmediately_ShouldBeUnhealthy()
    {
        // Arrange — a booting broker accepts and immediately drops connections.
        await using var listener = new ScriptedListener((client, _) =>
        {
            client.Close();
            return Task.CompletedTask;
        });
        var probe = new TcpReadyHealthCheck("localhost", listener.Port, TcpReadyHealthCheck.RedisHandshake);

        // Act
        var result = await probe.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenNothingListens_ShouldBeUnhealthy()
    {
        // Arrange — grab a free port and leave it closed.
        int freePort;
        using (var placeholder = new TcpListener(IPAddress.Loopback, 0))
        {
            placeholder.Start();
            freePort = ((IPEndPoint)placeholder.LocalEndpoint).Port;
            placeholder.Stop();
        }

        var probe = new TcpReadyHealthCheck("localhost", freePort, TcpReadyHealthCheck.RedisHandshake);

        // Act
        var result = await probe.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
