using System.Globalization;
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
    /// <param name="hostRoot">The SystemHost directory the development resolutions are declared
    /// relative to. Callers pass the same root they anchored discovery at (the current directory
    /// for the CLI verbs, the AppHost directory for Aspire).</param>
    public static GeneratedArtifacts Generate(SystemTopology topology, DevelopmentManifest development, string hostRoot)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(development);
        ArgumentException.ThrowIfNullOrEmpty(hostRoot);
        var files = new List<GeneratedFile>();

        // A transactional integration's internal queue is minted in the model, not declared as a
        // topic edge, so its pubsub does not emerge from the topics alone: a system of only
        // transactional integrations has no topics, yet still needs the system's pubsub.
        var pubSubNames = topology.Topics.Select(t => t.PubSubName)
            .Concat(topology.Components.Select(c => c.InternalQueue?.PubSubName).OfType<string>())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var pubSubName in pubSubNames)
        {
            files.Add(PubSubComponent(pubSubName, topology));
            files.Add(PubSubResiliency(pubSubName, topology));
        }

        // A processor keeps its state in two components of its own on one table: the plain one,
        // and the one whose transactional outbox publishes to the processor's one channel. Locally
        // both are PostgreSQL, which the local host starts when the topology has a processor;
        // deployment chooses its own store.
        foreach (var component in topology.Components)
        {
            if (component.State is { } state)
            {
                files.Add(StateComponent(state.StateStoreName, outbox: null, component.Name));
                files.Add(StateComponent(state.OutboxStateStoreName, state.Outbox, component.Name));
            }

            // A component that runs Dapr workflows keeps its instances in a state component of its
            // own, marked as the sidecar's actor state store. Locally the pubsub's Redis.
            if (component.WorkflowState is { } workflow)
            {
                var yaml = DaprYaml.Component(workflow.StateStoreName, "state.redis",
                    [("redisHost", "localhost:6380"), ("actorStateStore", "true")], [component.Name]);
                files.Add(new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{workflow.StateStoreName}.yaml", yaml));
            }
        }

        // Local runs resolve every port to a localstorage folder or, for a port whose deployed
        // binding reaches an HTTP API, to an HTTP binding on its mock; the deployed transport is
        // deployment-owned and never consulted here. The manifest is the single validation
        // boundary: it already rejects ports with neither resolution, so a miss here is an
        // invariant violation, not user input.
        var resolutions = development.Files.ToDictionary(file => file.PortName, StringComparer.Ordinal);
        var portMocks = development.Mocks.Where(mock => mock.Target == MockTarget.Port)
            .ToDictionary(mock => mock.Name, StringComparer.Ordinal);
        foreach (var port in topology.Ports)
        {
            files.Add(portMocks.TryGetValue(port.Name, out var portMock)
                ? MockedBindingComponent(port, portMock)
                : BindingComponent(port, resolutions[port.Name], hostRoot));
        }

        foreach (var mock in development.Mocks.Where(mock => mock.Target == MockTarget.Service))
        {
            var service = topology.Services.Single(service => service.AppId == mock.Name);
            files.Add(HttpEndpoint(mock, service));
        }

        // Every subscribing component receives through its gRPC app callback, pushed to by the
        // sidecar under a declarative Subscription: one routing rule per handled message (with its
        // content filter, if any), plus a default route for the channel's other messages. A transactional integration subscribes
        // to its own internal hop, which carries one kind of message, so it gets the default
        // route only.
        foreach (var component in topology.Components)
        {
            foreach (var subscription in component.Subscribes)
            {
                files.Add(SubscriptionResource(component.Name, subscription.PubSubName, subscription.TopicName,
                    [.. subscription.Messages.Select(m => (m, subscription.ConditionFor(m)))], subscription.Bulk));
            }

            if (component.InternalQueue is { } queue)
            {
                files.Add(SubscriptionResource(component.Name, queue.PubSubName, queue.TopicName, [], bulk: null));
            }
        }

        foreach (var component in topology.Components)
        {
            files.Add(ComponentConfig(topology.SystemName, component));
        }

        return new GeneratedArtifacts(files);
    }

    private static GeneratedFile PubSubComponent(string pubSubName, SystemTopology topology) =>
        PubSubComponent(pubSubName, PubSubScopes(pubSubName, topology));

    // A transactional integration must be in the scopes of the pubsub carrying its internal
    // queue: a component its sidecar cannot load leaves the queue unreachable.
    private static IEnumerable<string> PubSubScopes(string pubSubName, SystemTopology topology) =>
        topology.Components
            .Where(c => c.Subscribes.Any(s => s.PubSubName == pubSubName)
                || c.Publishes.Any(p => p.PubSubName == pubSubName)
                || c.InternalQueue?.PubSubName == pubSubName)
            .Select(c => c.Name);

    /// <summary>The name of the resiliency policy generated for <paramref name="pubSubName"/>.</summary>
    internal static string ResiliencyNameFor(string pubSubName) => $"{pubSubName}-resiliency";

    /// <summary>
    /// The pubsub's inbound retry policy. Without one, a delivery the component answers with RETRY
    /// (a technical failure: a store that is down, an ETag conflict to decide again) is dropped by the
    /// sidecar, not redelivered. With it, the sidecar redelivers it with exponential backoff, about a
    /// minute and a quarter in all, before giving up.
    /// </summary>
    private static GeneratedFile PubSubResiliency(string pubSubName, SystemTopology topology)
    {
        var name = ResiliencyNameFor(pubSubName);
        var yaml = DaprYaml.Resiliency(name, pubSubName, PubSubScopes(pubSubName, topology));
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{name}.yaml", yaml);
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

    private static GeneratedFile StateComponent(string name, MessageChannel? outbox, string componentName)
    {
        // The local PostgreSQL, not the pubsub's Redis. On Redis the outbox applies the state after
        // the commit returns, a write without an ETag overwrites, and a commit that loses an ETag
        // race can still publish: a local run lost updates and published events whose state was
        // lost, even one delivery at a time. PostgreSQL commits the state and the event together and
        // rejects a stale write. Both components use the store's default table; keys are prefixed
        // with the app id, so the plain and the outbox component address the same rows and
        // processors never share one. Port 6432 because a host install commonly holds 5432.
        var metadata = new List<(string, string)> { ("connectionString", LocalPostgresConnectionString) };
        if (outbox is not null)
        {
            metadata.Add(("outboxPublishPubsub", outbox.PubSubName));
            metadata.Add(("outboxPublishTopic", outbox.TopicName));
        }

        var yaml = DaprYaml.Component(name, "state.postgresql", metadata, [componentName], version: "v2");
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{name}.yaml", yaml);
    }

    /// <summary>The local PostgreSQL the processors' state components connect to: passwordless, as
    /// the local host starts it.</summary>
    internal const string LocalPostgresConnectionString =
        "host=localhost port=6432 user=postgres database=postgres sslmode=disable";

    private static GeneratedFile SubscriptionResource(string componentName, string pubSubName, string topicName,
        IReadOnlyList<(string Message, string? Condition)> messages, BulkSubscription? bulk)
    {
        var name = SubscriptionRouting.ResourceNameFor(componentName);
        var rules = messages.Select(m =>
            (SubscriptionRouting.MatchFor(m.Message, m.Condition), SubscriptionRouting.PathFor(m.Message)));
        var yaml = DaprYaml.Subscription(name, pubSubName, topicName, rules, SubscriptionRouting.UnhandledPath, bulk,
            [componentName]);
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{name}.yaml", yaml);
    }

    private static GeneratedFile BindingComponent(PortResource port, PortFileResolution resolution, string hostRoot)
    {
        // Local runs resolve every port to a localstorage folder. The manifest path is declared
        // relative to the SystemHost directory; the binding metadata must be absolute — the
        // artifacts land in per-run temp dirs and the sidecar's cwd is not guaranteed.
        var rootPath = Path.GetFullPath(Path.Combine(hostRoot, resolution.RootPath));
        var metadata = new List<(string, string)> { ("rootPath", rootPath) };
        var yaml = DaprYaml.Component(
            port.DaprComponentName, "bindings.localstorage", metadata, port.UsedBy);
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{port.DaprComponentName}.yaml", yaml);
    }

    private static GeneratedFile MockedBindingComponent(PortResource port, OpenApiMock mock)
    {
        // The binding keeps the port's name, so component code reaches the mock through the same
        // binding the deployment renders with the real API's url. bindings.http fails a non-2xx
        // answer by default, as it does deployed.
        var metadata = new List<(string, string)> { ("url", LocalMockEndpoints.BaseUri(mock)) };
        var yaml = DaprYaml.Component(port.DaprComponentName, "bindings.http", metadata, port.UsedBy);
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{port.DaprComponentName}.yaml", yaml);
    }

    private static GeneratedFile HttpEndpoint(OpenApiMock mock, ServiceResource service)
    {
        var yaml = DaprYaml.HttpEndpoint(mock.Name, LocalMockEndpoints.BaseUri(mock), service.Consumers);
        return new GeneratedFile($"{GeneratedArtifacts.ComponentsDir}/{mock.Name}.yaml", yaml);
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
                Subscribes = component.Subscribes.Select(s => new
                {
                    s.PubSubName,
                    s.TopicName,
                    s.Messages,
                    s.Conditions,
                    Unhandled = s.Unhandled.ToString(),
                    Bulk = s.Bulk is null
                        ? null
                        : new { s.Bulk.MaxMessages, MaxWaitMilliseconds = (long)s.Bulk.MaxWait.TotalMilliseconds },
                }),
                Publishes = component.Publishes.Select(p => new { p.PubSubName, p.TopicName, p.Message }),
                Ports = component.Ports.Select(c => new
                {
                    c.PortName,
                    Direction = c.Direction.ToString(),
                    DaprComponent = PortResource.DaprComponentNameFor(c.PortName),
                }),
                Uses = component.Uses,
                InternalQueue = component.InternalQueue is null
                    ? null
                    : new { component.InternalQueue.PubSubName, component.InternalQueue.TopicName },
                WorkflowStateStoreName = component.WorkflowState?.StateStoreName,
                State = component.State is null
                    ? null
                    : new
                    {
                        component.State.StateStoreName,
                        component.State.OutboxStateStoreName,
                        Outbox = component.State.Outbox is null
                            ? null
                            : new { component.State.Outbox.PubSubName, component.State.Outbox.TopicName },
                    },
            },
        };

        var json = JsonSerializer.Serialize(config, s_json);
        return new GeneratedFile($"{GeneratedArtifacts.ConfigDir}/{component.Name}.intropy.json", json);
    }
}

/// <summary>Minimal, deterministic emitter for Dapr <c>Component</c>, <c>HTTPEndpoint</c> and
/// <c>Subscription</c> YAML.</summary>
internal static class DaprYaml
{
    public static string Subscription(
        string name,
        string pubSubName,
        string topicName,
        IEnumerable<(string Match, string Path)> rules,
        string defaultPath,
        BulkSubscription? bulk,
        IEnumerable<string> scopes)
    {
        var ruleList = rules.ToList();
        var sb = new StringBuilder();
        sb.Append("apiVersion: dapr.io/v2alpha1\n");
        sb.Append("kind: Subscription\n");
        sb.Append("metadata:\n");
        sb.Append("  name: ").Append(Quote(name)).Append('\n');
        sb.Append("spec:\n");
        sb.Append("  pubsubname: ").Append(Quote(pubSubName)).Append('\n');
        sb.Append("  topic: ").Append(Quote(topicName)).Append('\n');
        sb.Append("  routes:\n");
        if (ruleList.Count > 0)
        {
            sb.Append("    rules:\n");
            foreach (var (match, path) in ruleList)
            {
                sb.Append("    - match: ").Append(Quote(match)).Append('\n');
                sb.Append("      path: ").Append(Quote(path)).Append('\n');
            }
        }

        sb.Append("    default: ").Append(Quote(defaultPath)).Append('\n');
        if (bulk is not null)
        {
            sb.Append("  bulkSubscribe:\n");
            sb.Append("    enabled: true\n");
            sb.Append("    maxMessagesCount: ").Append(bulk.MaxMessages.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("    maxAwaitDurationMs: ")
                .Append(((long)bulk.MaxWait.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        sb.Append("scopes:\n");
        foreach (var scope in scopes.OrderBy(scope => scope, StringComparer.Ordinal))
        {
            sb.Append("- ").Append(Quote(scope)).Append('\n');
        }

        return sb.ToString();
    }

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

    /// <summary>A Dapr <c>Resiliency</c> resource: an inbound retry, exponential backoff up to 10 s
    /// between attempts and 10 retries, on the deliveries of <paramref name="pubSubName"/>.</summary>
    public static string Resiliency(string name, string pubSubName, IEnumerable<string> scopes)
    {
        var sb = new StringBuilder();
        sb.Append("apiVersion: dapr.io/v1alpha1\n");
        sb.Append("kind: Resiliency\n");
        sb.Append("metadata:\n");
        sb.Append("  name: ").Append(Quote(name)).Append('\n');
        sb.Append("spec:\n");
        sb.Append("  policies:\n");
        sb.Append("    retries:\n");
        sb.Append("      redeliver:\n");
        sb.Append("        policy: exponential\n");
        sb.Append("        maxInterval: 10s\n");
        sb.Append("        maxRetries: 10\n");
        sb.Append("  targets:\n");
        sb.Append("    components:\n");
        sb.Append("      ").Append(Quote(pubSubName)).Append(":\n");
        sb.Append("        inbound:\n");
        sb.Append("          retry: redeliver\n");
        var scopeList = scopes.OrderBy(s => s, StringComparer.Ordinal).ToList();
        if (scopeList.Count > 0)
        {
            sb.Append("scopes:\n");
            foreach (var scope in scopeList)
                sb.Append("- ").Append(Quote(scope)).Append('\n');
        }

        return sb.ToString();
    }

    public static string Component(
        string name,
        string type,
        IEnumerable<(string Key, string Value)> metadata,
        IEnumerable<string> scopes,
        string version = "v1")
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
        sb.Append("  version: ").Append(version).Append('\n');
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
