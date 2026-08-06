// A deliberately minimal run-to-completion extractor, mirroring the real scaffold's shape:
// one sweep of work per run, then exit — and, like the scaffold's ExtractorRunner, it shuts
// its Dapr sidecar down in a finally so the whole pair reaches a terminal state. Activation
// cadence (a deployed CronJob schedule, a local re-run loop) lives in the scaffold and
// deployment configuration, not in the topology.

var component = Environment.GetEnvironmentVariable("INTROPY__COMPONENT") ?? "unknown";
var config = Environment.GetEnvironmentVariable("INTROPY__CONFIG");

try
{
    Console.WriteLine($"Intropy component {component}: run started at {DateTimeOffset.Now:O}. Runtime config: {config}");
    await Task.Delay(TimeSpan.FromSeconds(15));
    Console.WriteLine($"Intropy component {component}: run finished at {DateTimeOffset.Now:O}");
}
finally
{
    // Mirror the scaffold's ShutdownSidecarAsync without a Dapr SDK dependency: a
    // run-to-completion block takes its sidecar down with it so the run ends cleanly.
    var daprPort = Environment.GetEnvironmentVariable("DAPR_HTTP_PORT");
    if (daprPort is not null)
    {
        try
        {
            // Short timeout: if the sidecar never came up, its allocated port may accept
            // the connection and then hang — a best-effort shutdown must not stall the run.
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            await http.PostAsync(new Uri($"http://localhost:{daprPort}/v1.0/shutdown"), content: null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The sidecar is already gone (or was never there) — nothing to shut down.
        }
    }
}
