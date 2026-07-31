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
    public static Task<int> RunAsync(Assembly assembly, string[] args)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(args);

        var verb = args.Length > 0 ? args[0] : "check";
        return Task.FromResult(verb switch
        {
            "check" => Check(assembly),
            "graph" => Graph(assembly),
            "generate" => Generate(assembly, args),
            _ => Unknown(verb),
        });
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
        catch (Exception ex) when (ex is TopologyValidationException or DevelopmentValidationException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Graph(Assembly assembly)
    {
        try
        {
            var topology = SystemDiscovery.Discover(assembly).Topology;
            Console.WriteLine(JsonSerializer.Serialize(GraphDocument.From(topology), s_json));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Maps the validated internal model to the topology.intropy.io/v1 interchange contract.</summary>
    private sealed record GraphDocument(
        string ApiVersion,
        string Kind,
        string System,
        IReadOnlyList<GraphComponent>? Components,
        IReadOnlyList<GraphTopic>? Topics,
        IReadOnlyList<GraphConnector>? Connectors,
        IReadOnlyList<GraphService>? Services)
    {
        public static GraphDocument From(SystemTopology topology) => new(
            "topology.intropy.io/v1",
            "SystemTopology",
            topology.SystemName,
            Optional(topology.Components.Select(GraphComponent.From)),
            Optional(topology.Topics.Select(GraphTopic.From)),
            Optional(topology.Connectors.Select(GraphConnector.From)),
            Optional(topology.Services.Select(GraphService.From)));
    }

    private sealed record GraphComponent(
        string Name,
        string Kind,
        IReadOnlyList<GraphTopicReference>? Subscribes,
        IReadOnlyList<GraphPublication>? Publishes,
        IReadOnlyList<GraphConnectorUse>? Connectors,
        IReadOnlyList<string>? Uses)
    {
        public static GraphComponent From(ComponentModel component) => new(
            component.Name,
            KebabCase(component.Kind.ToString()),
            Optional(component.Subscribes.Select(t => new GraphTopicReference(t.PubSubName, t.TopicName))),
            Optional(component.Publishes.Select(p => new GraphPublication(
                p.Port == "default" ? null : p.Port, p.PubSubName, p.TopicName))),
            Optional(component.Connectors.Select(c => new GraphConnectorUse(
                c.ConnectorName, Direction(c.Direction)))),
            Optional(component.Uses));
    }

    private sealed record GraphTopicReference(
        [property: JsonPropertyName("pubsub")] string PubSub,
        string Topic);

    private sealed record GraphPublication(
        string? Port,
        [property: JsonPropertyName("pubsub")] string PubSub,
        string Topic);

    private sealed record GraphConnectorUse(string Connector, string Direction);

    private sealed record GraphTopic(
        [property: JsonPropertyName("pubsub")] string PubSub,
        string Topic,
        string Contract,
        IReadOnlyList<string>? Publishers,
        IReadOnlyList<string>? Subscribers)
    {
        public static GraphTopic From(TopicResource topic) => new(
            topic.PubSubName,
            topic.TopicName,
            topic.ContractTypeName,
            Optional(topic.Publishers),
            Optional(topic.Subscribers));
    }

    private sealed record GraphConnector(
        string Name,
        GraphTransport Transport,
        GraphTransport? DeployedTransport,
        IReadOnlyList<string>? Directions,
        IReadOnlyList<string>? UsedBy)
    {
        public static GraphConnector From(ConnectorResource connector) => new(
            connector.Name,
            GraphTransport.From(connector.Transport),
            connector.DeployedTransport is { } deployedTransport ? GraphTransport.From(deployedTransport) : null,
            Optional(connector.Directions.Select(Direction)),
            Optional(connector.UsedBy));
    }

    private sealed record GraphService(string AppId, IReadOnlyList<string>? Consumers)
    {
        public static GraphService From(ServiceResource service) => new(service.AppId, Optional(service.Consumers));
    }

    private sealed record GraphTransport(string Type, bool SupportsInput, bool SupportsOutput)
    {
        public static GraphTransport From(Transport transport) => transport switch
        {
            FileTransport => new("file", transport.SupportsInput, transport.SupportsOutput),
            SftpTransport => new("sftp", transport.SupportsInput, transport.SupportsOutput),
            _ => throw new InvalidOperationException($"Unsupported graph transport '{transport.GetType().Name}'."),
        };
    }

    private static T[]? Optional<T>(IEnumerable<T> values)
    {
        var array = values.ToArray();
        return array.Length > 0 ? array : null;
    }

    private static string Direction(ConnectorDirection direction) =>
        direction == ConnectorDirection.In ? "in" : "out";

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
        try
        {
            var directory = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-')) ?? "./out";
            var discovered = SystemDiscovery.Discover(assembly);
            var development = DevelopmentDiscovery.Discover(assembly, discovered.Topology, Directory.GetCurrentDirectory());
            var artifacts = TopologyGenerator.Generate(discovered.Topology, development);
            artifacts.WriteTo(directory);

            Console.WriteLine($"generated {artifacts.Files.Count} files to '{directory}':");
            foreach (var file in artifacts.Files)
            {
                Console.WriteLine($"  {file.RelativePath}");
            }

            return 0;
        }
        catch (Exception ex) when (ex is TopologyValidationException or DevelopmentValidationException or InvalidOperationException)
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
