using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation;

/// <summary>A single generated artifact: a relative path and its text content.</summary>
/// <param name="RelativePath">Path relative to the output root (forward slashes).</param>
/// <param name="Content">The file's text content.</param>
public sealed record GeneratedFile(string RelativePath, string Content);

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
    // Schedule is the only emitted property that is ever null; omitting it keeps
    // unscheduled components' config free of a redundant attribute.
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Generates the artifacts for a topology.</summary>
    /// <param name="topology">The validated topology.</param>
    /// <returns>The generated artifacts.</returns>
    public static GeneratedArtifacts Generate(SystemTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        var files = new List<GeneratedFile>();

        foreach (var topic in topology.Topics.Select(t => t.PubSubName).Distinct(StringComparer.Ordinal))
        {
            files.Add(PubSubComponent(topic, topology));
        }

        foreach (var connector in topology.Connectors)
        {
            files.Add(BindingComponent(connector));
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
            .Select(c => c.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

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

    private static GeneratedFile BindingComponent(ConnectorResource connector)
    {
        var metadata = new List<(string, string)>();
        if (connector.Transport is FileTransport file)
        {
            // Artifacts land in per-run temp dirs and the sidecar's cwd is not guaranteed,
            // so anchor the folder at generation time (cwd = the SystemHost directory).
            metadata.Add(("rootPath", Path.GetFullPath(file.RootPath)));
        }

        var yaml = DaprYaml.Component(
            connector.DaprComponentName, connector.Transport.DaprType, metadata, connector.UsedBy);
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{connector.DaprComponentName}.yaml", yaml);
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
                component.Schedule,
                Subscribes = component.Subscribes.Select(s => new { s.PubSubName, s.TopicName }),
                Publishes = component.Publishes.Select(p => new { p.Port, p.PubSubName, p.TopicName }),
                Connectors = component.Connectors.Select(c => new
                {
                    c.ConnectorName,
                    Direction = c.Direction.ToString(),
                    DaprComponent = $"binding.{c.ConnectorName}",
                }),
            },
        };

        var json = JsonSerializer.Serialize(config, s_json);
        return new GeneratedFile($"{GeneratedArtifacts.ConfigDir}/{component.Name}.intropy.json", json);
    }
}

/// <summary>Minimal, deterministic emitter for Dapr <c>Component</c> YAML.</summary>
internal static class DaprYaml
{
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
