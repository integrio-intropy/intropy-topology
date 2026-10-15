using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation;

/// <summary>
/// The non-Aspire backend: the <c>check</c>, <c>graph</c>, and <c>generate</c> CLI verbs over a
/// discovered system. F5 (the Aspire backend) and these verbs consume the same discovered truth.
/// </summary>
public static class IntropyGenerate
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Runs a generation CLI verb against the system discovered in <paramref name="assembly"/>.</summary>
    /// <param name="assembly">The assembly declaring the <see cref="ISystemDefinition"/>.</param>
    /// <param name="args">Command-line arguments; <c>args[0]</c> is the verb (check | graph | generate).</param>
    /// <returns>A process exit code.</returns>
    public static int Run(Assembly assembly, string[] args)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(args);

        var verb = args.Length > 0 ? args[0] : "check";
        return verb switch
        {
            "check" => Check(assembly),
            "graph" => Graph(assembly, args),
            "generate" => Generate(assembly, args),
            _ => Unknown(verb),
        };
    }

    private static int Check(Assembly assembly)
    {
        try
        {
            var discovered = SystemDiscovery.Discover(assembly);
            DevelopmentDiscovery.Discover(assembly, discovered.Topology, Directory.GetCurrentDirectory());
            Console.WriteLine($"ok: '{discovered.Topology.SystemName}' is valid ({discovered.Topology.Components.Count} components).");
            return 0;
        }
        catch (Exception ex) when (ex is IntropyException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Graph(Assembly assembly, string[] args)
    {
        try
        {
            // Tolerant discovery: a partially built system (one-sided topics) must still be
            // inspectable. Warnings go to stderr so stdout stays pure JSON.
            var topology = SystemDiscovery.DiscoverTolerant(assembly, out var diagnostics).Topology;
            foreach (var diagnostic in diagnostics)
            {
                var severity = diagnostic.Severity == DiagnosticSeverity.Warning ? "warning" : "error";
                Console.Error.WriteLine($"{severity}: {diagnostic.Message}");
            }

            var development = args.Contains("--development", StringComparer.Ordinal)
                ? DevelopmentDiscovery.Discover(assembly, topology, Directory.GetCurrentDirectory())
                : null;
            Console.WriteLine(GraphJson(topology, development));
            return 0;
        }
        catch (Exception ex) when (ex is IntropyException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Serializes <paramref name="topology"/> as a topology.intropy.io/v1 document.</summary>
    internal static string GraphJson(SystemTopology topology, DevelopmentManifest? development) =>
        JsonSerializer.Serialize(GraphDocument.From(topology, development), s_json);

    /// <summary>Maps the validated internal model to the topology.intropy.io/v1 interchange contract.</summary>
    private sealed record GraphDocument(
        string ApiVersion,
        string Kind,
        string System,
        IReadOnlyList<GraphComponent>? Components,
        IReadOnlyList<GraphTopic>? Topics,
        [property: JsonPropertyName("messagegroups")] IReadOnlyList<GraphMessageGroup> MessageGroups,
        IReadOnlyList<GraphPort>? Ports,
        IReadOnlyList<GraphService>? Services,
        GraphDevelopment? Development)
    {
        public static GraphDocument From(SystemTopology topology, DevelopmentManifest? development) => new(
            "topology.intropy.io/v1",
            "SystemTopology",
            topology.SystemName,
            Optional(topology.Components.Select(GraphComponent.From)),
            Optional(topology.Topics.Select(GraphTopic.From)),
            // Message groups are the message-level view of the same edges the topics carry,
            // not an optional section: an empty topology still declares messagegroups: [].
            [.. topology.MessageGroups.Select(GraphMessageGroup.From)],
            Optional(topology.Ports.Select(GraphPort.From)),
            Optional(topology.Services.Select(GraphService.From)),
            development is null ? null : GraphDevelopment.From(development));
    }

    private sealed record GraphDevelopment(
        IReadOnlyList<GraphMock>? Mocks,
        IReadOnlyList<GraphFilePort>? Files)
    {
        public static GraphDevelopment From(DevelopmentManifest development) => new(
            Optional(development.Mocks.Select(GraphMock.From)),
            Optional(development.Files.Select(GraphFilePort.From)));
    }

    private sealed record GraphMock(string AppId, string Artifact, string Title, string Version, string BaseUri)
    {
        public static GraphMock From(OpenApiMock mock) => new(
            mock.AppId, mock.ArtifactPath, mock.Title, mock.Version, LocalMockEndpoints.BaseUri(mock));
    }

    private sealed record GraphFilePort(string Port, string RootPath)
    {
        public static GraphFilePort From(PortFileResolution file) => new(file.PortName, file.RootPath);
    }

    private sealed record GraphComponent(
        string Name,
        string Kind,
        IReadOnlyList<GraphSubscription>? Subscribes,
        IReadOnlyList<GraphPublication>? Publishes,
        IReadOnlyList<GraphPortUse>? Ports,
        IReadOnlyList<string>? Uses,
        GraphInternalQueue? InternalQueue)
    {
        public static GraphComponent From(ComponentModel component) => new(
            component.Name,
            KebabCase(component.Kind.ToString()),
            Optional(component.Subscribes.Select(s => GraphSubscription.From(component.Name, s))),
            Optional(component.Publishes.Select(p => new GraphPublication(
                p.PubSubName, p.TopicName, string.IsNullOrEmpty(p.Message) ? null : p.Message))),
            Optional(component.Ports.Select(c => new GraphPortUse(
                c.PortName, Direction(c.Direction)))),
            Optional(component.Uses),
            component.InternalQueue is null
                ? null
                : new GraphInternalQueue(component.InternalQueue.PubSubName, component.InternalQueue.TopicName,
                    SubscriptionRouting.ResourceNameFor(component.Name), SubscriptionRouting.UnhandledPath));
    }

    /// <summary>A transactional integration's internal hop, and the <c>Subscription</c> it receives
    /// it through: one kind of message, so no rules — every event takes the default path.</summary>
    private sealed record GraphInternalQueue(
        [property: JsonPropertyName("pubsub")] string PubSub,
        string Topic,
        string Resource,
        string DefaultPath);

    /// <summary>A subscription: its channel, its routes in the order the sidecar evaluates them —
    /// one per rule of the rendered Dapr <c>Subscription</c> — and what happens to the channel's
    /// events no route matches. It also carries the <c>Subscription</c> resource as
    /// <see cref="SubscriptionRouting"/> renders it — the resource name, each rule's match and
    /// path, the default path, the bulk settings — so a deployment renders the exact resource a
    /// local run loads, without re-deriving the conventions.</summary>
    private sealed record GraphSubscription(
        [property: JsonPropertyName("pubsub")] string PubSub,
        string Topic,
        IReadOnlyList<GraphRoute>? Routes,
        string Default,
        string Resource,
        string DefaultPath,
        GraphBulk? Bulk)
    {
        public static GraphSubscription From(string componentName, TopicSubscription subscription) => new(
            subscription.PubSubName,
            subscription.TopicName,
            Optional(subscription.Messages.Select(m => new GraphRoute(m, subscription.ConditionFor(m),
                SubscriptionRouting.MatchFor(m, subscription.ConditionFor(m)), SubscriptionRouting.PathFor(m)))),
            subscription.Unhandled switch
            {
                UnhandledMessages.Ignore => "ignore",
                UnhandledMessages.DeadLetter => "dead-letter",
                _ => throw new InvalidOperationException($"unknown unhandled-message handling '{subscription.Unhandled}'."),
            },
            SubscriptionRouting.ResourceNameFor(componentName),
            SubscriptionRouting.UnhandledPath,
            subscription.Bulk is { } bulk
                ? new GraphBulk(bulk.MaxMessages, (long)bulk.MaxWait.TotalMilliseconds)
                : null);
    }

    /// <summary>A route: the message it handles (its name is its CloudEvent type), the content
    /// filter its events must also match, if any, and the rule the <c>Subscription</c> renders for
    /// it — its CEL match and the path it delivers on.</summary>
    private sealed record GraphRoute(string Message, string? When, string Match, string Path);

    /// <summary>A bulk subscription's batching, in the units the <c>Subscription</c> resource takes.</summary>
    private sealed record GraphBulk(int MaxMessagesCount, long MaxAwaitDurationMs);

    private sealed record GraphPublication(
        [property: JsonPropertyName("pubsub")] string PubSub,
        string Topic,
        string? Message);

    private sealed record GraphPortUse(string Port, string Direction);

    private sealed record GraphTopic(
        [property: JsonPropertyName("pubsub")] string PubSub,
        string Topic,
        IReadOnlyList<string>? Messages,
        IReadOnlyList<string>? Publishers,
        IReadOnlyList<string>? Subscribers)
    {
        // No contract: a topic can carry several messages; each one's contract is in messagegroups.
        public static GraphTopic From(TopicResource topic) => new(
            topic.PubSubName,
            topic.TopicName,
            Optional(topic.Messages),
            Optional(topic.Publishers),
            Optional(topic.Subscribers));
    }

    private sealed record GraphPort(
        string Name,
        IReadOnlyList<string>? Directions,
        IReadOnlyList<string>? UsedBy)
    {
        public static GraphPort From(PortResource port) => new(
            port.Name,
            Optional(port.Directions.Select(Direction)),
            Optional(port.UsedBy));
    }

    private sealed record GraphMessageGroup(
        string Name,
        IReadOnlyList<GraphMessage> Messages)
    {
        public static GraphMessageGroup From(MessageGroupResource group) => new(
            group.Name,
            [.. group.Messages.Select(GraphMessage.From)]);
    }

    private sealed record GraphMessage(
        string Name,
        string Contract,
        GraphMessageChannel Channel,
        IReadOnlyList<string>? Publishers,
        IReadOnlyList<string>? Subscribers)
    {
        public static GraphMessage From(MessageResource message) => new(
            message.Name,
            message.ContractTypeName,
            GraphMessageChannel.From(message.Channel),
            Optional(message.Publishers),
            Optional(message.Subscribers));
    }

    private sealed record GraphMessageChannel(
        [property: JsonPropertyName("pubsub")] string PubSub,
        string Topic)
    {
        public static GraphMessageChannel From(MessageChannel channel) => new(
            channel.PubSubName,
            channel.TopicName);
    }

    private sealed record GraphService(string AppId, IReadOnlyList<string>? Consumers)
    {
        public static GraphService From(ServiceResource service) => new(service.AppId, Optional(service.Consumers));
    }

    private static T[]? Optional<T>(IEnumerable<T> values)
    {
        var array = values.ToArray();
        return array.Length > 0 ? array : null;
    }

    private static string Direction(PortDirection direction) =>
        direction == PortDirection.In ? "in" : "out";

    private static string KebabCase(string pascal)
    {
        var builder = new System.Text.StringBuilder(pascal.Length + 4);
        foreach (var c in pascal)
        {
            if (char.IsUpper(c) && builder.Length > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static int Generate(Assembly assembly, string[] args)
    {
        var positional = args.Skip(1).Where(a => !a.StartsWith('-')).ToArray();
        if (positional.Length > 1)
        {
            Console.Error.WriteLine($"unrecognized arguments: {string.Join(' ', positional.Skip(1))}. Expected: generate [directory].");
            return 2;
        }

        try
        {
            var directory = positional.FirstOrDefault() ?? "./out";
            var hostRoot = Directory.GetCurrentDirectory();
            var discovered = SystemDiscovery.Discover(assembly);
            var development = DevelopmentDiscovery.Discover(assembly, discovered.Topology, hostRoot);
            var artifacts = TopologyGenerator.Generate(discovered.Topology, development, hostRoot);
            artifacts.WriteTo(directory);

            Console.WriteLine($"generated {artifacts.Files.Count} files to '{directory}':");
            foreach (var file in artifacts.Files)
            {
                Console.WriteLine($"  {file.RelativePath}");
            }

            return 0;
        }
        catch (Exception ex) when (ex is IntropyException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'. Expected: check | graph | generate.");
        return 2;
    }
}
