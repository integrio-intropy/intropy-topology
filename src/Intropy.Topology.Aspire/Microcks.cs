using System.Net.Http.Headers;
using System.Text.Json;
using Intropy.Topology.Generation;
using Intropy.Topology.Model;
using Microsoft.Extensions.Logging;

namespace Intropy.Topology.Aspire;

/// <summary>Per-mock readiness gates — one per mocked service and per mocked port — completed only
/// after Microcks imports each contract.</summary>
internal sealed class MockReadiness(DevelopmentManifest development)
{
    private readonly Dictionary<(MockTarget Target, string Name), TaskCompletionSource> _gates = development.Mocks.ToDictionary(
        mock => (mock.Target, mock.Name),
        _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>Whether any of the component's ports is mocked.</summary>
    public bool MocksAnyPortOf(ComponentModel component) =>
        component.Ports.Any(port => _gates.ContainsKey((MockTarget.Port, port.PortName)));

    /// <summary>Waits until the contracts of every mocked service the component calls and every
    /// mocked port it uses are imported.</summary>
    public async Task WaitForAsync(ComponentModel component, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var subjects = component.Uses.Select(appId => (MockTarget.Service, appId))
            .Concat(component.Ports.Select(port => (MockTarget.Port, port.PortName)));
        var waits = subjects.Where(_gates.ContainsKey).Select(subject => _gates[subject].Task).ToArray();
        if (waits.Length == 0)
        {
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        await Task.WhenAll(waits).WaitAsync(deadline.Token).ConfigureAwait(false);
    }

    public void Complete(OpenApiMock mock) => _gates[(mock.Target, mock.Name)].TrySetResult();

    public void Fail(OpenApiMock mock, Exception exception) => _gates[(mock.Target, mock.Name)].TrySetException(exception);
}

/// <summary>Imports and verifies all local mock contracts before their consumers start.</summary>
internal sealed class MicrocksImporter(
    DevelopmentManifest development,
    MockReadiness readiness,
    IHttpClientFactory clients,
    ILogger<MicrocksImporter> logger)
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(3);

    internal async Task ImportAsync(CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(nameof(MicrocksImporter));
        client.BaseAddress = new Uri(IntropyAspire.MicrocksOrigin);
        foreach (var mock in development.Mocks)
        {
            try
            {
                await ImportAndVerifyAsync(client, mock, cancellationToken).ConfigureAwait(false);
                readiness.Complete(mock);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var subject = mock.Target == MockTarget.Port ? "port" : "service";
                readiness.Fail(mock, new InvalidOperationException(
                    $"Microcks could not import or verify {subject} '{mock.Name}' from '{mock.ArtifactPath}'.", ex));
                logger.LogError(ex, "intropy-microcks: could not prepare {Subject} {Name} from {Artifact}", subject, mock.Name, mock.ArtifactPath);
            }
        }
    }

    private static async Task ImportAndVerifyAsync(HttpClient client, OpenApiMock mock, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(s_timeout);
        while (true)
        {
            try
            {
                using var health = await client.GetAsync(new Uri("/api/health", UriKind.Relative), deadline.Token).ConfigureAwait(false);
                if (health.IsSuccessStatusCode)
                {
                    break;
                }
            }
            catch (HttpRequestException) when (!deadline.IsCancellationRequested)
            {
                // The published endpoint is available before the Uber process is ready.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token).ConfigureAwait(false);
        }

        await using var stream = File.OpenRead(mock.ArtifactPath);
        using var content = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/yaml");
        content.Add(file, "file", Path.GetFileName(mock.ArtifactPath));
        using var upload = await client.PostAsync(new Uri("/api/artifact/upload?mainArtifact=true", UriKind.Relative), content, deadline.Token).ConfigureAwait(false);
        if (upload.StatusCode != System.Net.HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Microcks artifact upload returned {(int)upload.StatusCode} ({upload.ReasonPhrase}).");
        }

        while (true)
        {
            using var response = await client.GetAsync(new Uri("/api/services?page=0&size=100", UriKind.Relative), deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var services = await JsonDocument.ParseAsync(responseStream, cancellationToken: deadline.Token).ConfigureAwait(false);
            var matches = CountMatchingServices(services.RootElement, mock);
            if (matches == 1)
            {
                return;
            }

            if (matches > 1)
            {
                throw new InvalidOperationException("Microcks returned more than one matching REST service.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token).ConfigureAwait(false);
        }
    }

    internal static int CountMatchingServices(JsonElement root, OpenApiMock mock)
    {
        var entries = root;
        if (entries.ValueKind == JsonValueKind.Object && entries.TryGetProperty("content", out var contentProperty))
        {
            entries = contentProperty;
        }

        if (entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Microcks services response was not an array.");
        }

        return entries.EnumerateArray().Count(service =>
        {
            if (!service.TryGetProperty("name", out var name)
                || !service.TryGetProperty("version", out var version)
                || !service.TryGetProperty("type", out var type))
            {
                throw new InvalidOperationException(
                    "Microcks /api/services returned an entry missing 'name', 'version', or 'type'.");
            }

            return name.GetString() == mock.Title
                && version.GetString() == mock.Version
                && type.GetString() == "REST";
        });
    }
}
