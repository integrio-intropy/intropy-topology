using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation;

/// <summary>A single generated artifact: a relative path and its text content.</summary>
/// <param name="RelativePath">Path relative to the output root (forward slashes).</param>
/// <param name="Content">The file's text content.</param>
public sealed record GeneratedFile(string RelativePath, string Content);

/// <summary>
/// The single composition point for local mock endpoint URLs. The origin is a
/// local-runtime convention this dependency-free package would rather not know —
/// but the <c>topology.intropy.io/v1</c> graph contract includes <c>baseUri</c>,
/// which forces the knowledge here. The Aspire backend owns the matching port
/// constant (<c>IntropyAspire.MicrocksPort</c>) and must agree with
/// <see cref="Origin"/>; do not introduce a second composition site.
/// </summary>
internal static class LocalMockEndpoints
{
    public const string Origin = "http://localhost:8585";

    /// <summary>The mock's local Microcks REST base URI, with title and version escaped
    /// as individual path segments.</summary>
    public static string BaseUri(OpenApiMock mock) =>
        $"{Origin}/rest/{Uri.EscapeDataString(mock.Title)}/{Uri.EscapeDataString(mock.Version)}";
}

/// <summary>The artifacts generated for a system: Dapr component YAML and per-component runtime config.</summary>
public sealed class GeneratedArtifacts
{
    /// <summary>Subdirectory holding Dapr component YAML — the value for a sidecar's resources-path.</summary>
    public const string ComponentsDir = "components";

    /// <summary>Subdirectory holding per-component runtime config.</summary>
    public const string ConfigDir = "config";

    internal GeneratedArtifacts(IReadOnlyList<GeneratedFile> files) => Files = files;

    /// <summary>All generated files, in deterministic order.</summary>
    public IReadOnlyList<GeneratedFile> Files { get; }

    /// <summary>Writes every artifact under <paramref name="directory"/>, creating subdirectories.</summary>
    /// <param name="directory">The output root.</param>
    public void WriteTo(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        foreach (var file in Files)
        {
            var path = Path.Combine(directory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Content);
        }
    }
}

/// <summary>
/// Turns a validated <see cref="SystemTopology"/> into Dapr components and runtime config
/// for the local run profile: pub/sub backed by Redis Streams, file bindings anchored to the
/// SystemHost directory. Output order is deterministic.
/// </summary>
public static class TopologyGenerator
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Generates local artifacts, including validated development mock endpoints when declared.</summary>
    /// <param name="topology">The validated topology.</param>
    /// <param name="development">The optional validated development substitutions.</param>
    public static GeneratedArtifacts Generate(SystemTopology topology, DevelopmentManifest development)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(development);
        var files = new List<GeneratedFile>();

        foreach (var topic in topology.Topics.Select(t => t.PubSubName).Distinct(StringComparer.Ordinal))
        {
            files.Add(PubSubComponent(topic, topology));
        }

        // The transactional integration's internal hop is minted in the model, not declared as
        // a topic edge, so its pubsub does not emerge from the Topics loop above. Same Redis
        // backend as the inter-component pubsubs; scoped to exactly the owning component.
        foreach (var component in topology.Components.Where(c => c.InternalQueue is not null))
        {
            files.Add(PubSubComponent(component.InternalQueue!.PubSubName, [component.Name]));
        }

        // Local runs resolve every connector to localstorage, so the declared transport is
        // deliberately not consulted here: the placeholder default transport must not block
        // F5. Deployment generation is where an unresolved transport must hard-fail.
        // The manifest is the single validation boundary: it already rejects unresolved
        // connectors, so a miss here is an invariant violation, not user input.
        var resolutions = development.Files.ToDictionary(file => file.ConnectorName, StringComparer.Ordinal);
        foreach (var connector in topology.Connectors)
        {
            files.Add(BindingComponent(connector, resolutions[connector.Name]));
        }

        foreach (var mock in development.Mocks)
        {
            var service = topology.Services.Single(service => service.AppId == mock.AppId);
            files.Add(HttpEndpoint(mock, service));
        }

        foreach (var component in topology.Components)
        {
            files.Add(ComponentConfig(topology.SystemName, component));
        }

        return new GeneratedArtifacts(files);
    }

    private static GeneratedFile PubSubComponent(string pubSubName, SystemTopology topology)
    {
        var scopes = topology.Components
            .Where(c => c.Subscribes.Any(s => s.PubSubName == pubSubName) || c.Publishes.Any(p => p.PubSubName == pubSubName))
            .Select(c => c.Name);
        return PubSubComponent(pubSubName, scopes);
    }

    private static GeneratedFile PubSubComponent(string pubSubName, IEnumerable<string> scopes)
    {
        scopes = scopes.OrderBy(n => n, StringComparer.Ordinal);

        // Redis Streams with consumer groups: an unacked or RETRY'd message is redelivered
        // after the component's redeliverInterval instead of being dropped. Port 6380 because
        // `dapr init` publishes its own dapr_redis on the host's 6379.
        var metadata = new[]
        {
            ("redisHost", "localhost:6380"),
        };
        var yaml = DaprYaml.Component(pubSubName, "pubsub.redis", metadata, scopes);
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{pubSubName}.yaml", yaml);
    }

    private static GeneratedFile BindingComponent(ConnectorResource connector, ConnectorFileResolution resolution)
    {
        // Local runs resolve every connector to a localstorage folder. The manifest path is
        // already absolute (anchored at the SystemHost directory); artifacts land in per-run
        // temp dirs and the sidecar's cwd is not guaranteed, so an absolute path is required.
        var metadata = new List<(string, string)> { ("rootPath", resolution.RootPath) };
        var yaml = DaprYaml.Component(
            connector.DaprComponentName, "bindings.localstorage", metadata, connector.UsedBy);
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{connector.DaprComponentName}.yaml", yaml);
    }

    private static GeneratedFile HttpEndpoint(OpenApiMock mock, ServiceResource service)
    {
        var yaml = DaprYaml.HttpEndpoint(mock.AppId, LocalMockEndpoints.BaseUri(mock), service.Consumers);
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{mock.AppId}.yaml", yaml);
    }

    private static GeneratedFile ComponentConfig(string systemName, ComponentModel component)
    {
        var config = new
        {
            Intropy = new
            {
                System = systemName,
                Component = component.Name,
                Kind = component.Kind.ToString(),
                Subscribes = component.Subscribes.Select(s => new { s.PubSubName, s.TopicName }),
                Publishes = component.Publishes.Select(p => new { p.Port, p.PubSubName, p.TopicName }),
                Connectors = component.Connectors.Select(c => new
                {
                    c.ConnectorName,
                    Direction = c.Direction.ToString(),
                    DaprComponent = ConnectorResource.DaprComponentNameFor(c.ConnectorName),
                }),
                Uses = component.Uses,
                InternalQueue = component.InternalQueue is null
                    ? null
                    : new { component.InternalQueue.PubSubName, component.InternalQueue.TopicName },
            },
        };

        var json = JsonSerializer.Serialize(config, s_json);
        return new GeneratedFile($"{GeneratedArtifacts.ConfigDir}/{component.Name}.intropy.json", json);
    }
}

/// <summary>Minimal, deterministic emitter for Dapr <c>Component</c> YAML.</summary>
internal static class DaprYaml
{
    public static string HttpEndpoint(string name, string baseUrl, IEnumerable<string> scopes)
    {
        var sb = new StringBuilder();
        sb.Append("apiVersion: dapr.io/v1alpha1\n");
        sb.Append("kind: HTTPEndpoint\n");
        sb.Append("metadata:\n");
        sb.Append("  name: ").Append(Quote(name)).Append('\n');
        sb.Append("spec:\n");
        sb.Append("  baseUrl: ").Append(Quote(baseUrl)).Append('\n');
        sb.Append("scopes:\n");
        foreach (var scope in scopes.OrderBy(scope => scope, StringComparer.Ordinal))
        {
            sb.Append("- ").Append(Quote(scope)).Append('\n');
        }

        return sb.ToString();
    }

    public static string Component(
        string name,
        string type,
        IEnumerable<(string Key, string Value)> metadata,
        IEnumerable<string> scopes)
    {
        var meta = metadata.ToList();
        var scopeList = scopes.ToList();

        var sb = new StringBuilder();
        sb.Append("apiVersion: dapr.io/v1alpha1\n");
        sb.Append("kind: Component\n");
        sb.Append("metadata:\n");
        sb.Append("  name: ").Append(Quote(name)).Append('\n');
        sb.Append("spec:\n");
        sb.Append("  type: ").Append(Quote(type)).Append('\n');
        sb.Append("  version: v1\n");
        if (meta.Count == 0)
        {
            sb.Append("  metadata: []\n");
        }
        else
        {
            sb.Append("  metadata:\n");
            foreach (var (key, value) in meta)
            {
                sb.Append("  - name: ").Append(Quote(key)).Append('\n');
                sb.Append("    value: ").Append(Quote(value)).Append('\n');
            }
        }

        if (scopeList.Count > 0)
        {
            sb.Append("scopes:\n");
            foreach (var scope in scopeList)
            {
                sb.Append("- ").Append(Quote(scope)).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
