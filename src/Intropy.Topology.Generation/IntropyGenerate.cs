using System.Reflection;
using System.Text.Json;
using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation;

/// <summary>
/// The non-Aspire backend: the <c>check</c>, <c>graph</c>, and <c>generate</c> CLI verbs over a
/// discovered system. F5 (the Aspire backend) and these verbs consume the same discovered truth.
/// </summary>
public static class IntropyGenerate
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

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
            Console.WriteLine($"ok: '{discovered.Topology.SystemName}' is valid ({discovered.Topology.Components.Count} components).");
            return 0;
        }
        catch (TopologyValidationException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Graph(Assembly assembly)
    {
        var discovered = SystemDiscovery.Discover(assembly);
        Console.WriteLine(JsonSerializer.Serialize(DisplayModel(discovered.Topology), s_json));
        return 0;
    }

    /// <summary>
    /// The graph verb's human-facing projection of the model: camelCase, kebab-case kinds,
    /// short contract names, empty collections omitted. The wire format (plain model
    /// serialization) is unchanged — this is presentation, not interchange.
    /// </summary>
    private static Dictionary<string, object> DisplayModel(SystemTopology topology)
    {
        var model = new Dictionary<string, object>
        {
            ["systemName"] = topology.SystemName,
            ["components"] = topology.Components.Select(DisplayComponent).ToArray(),
            ["topics"] = topology.Topics.Select(t => new Dictionary<string, object>
            {
                ["pubsub"] = t.PubSubName,
                ["name"] = t.TopicName,
                ["contract"] = ShortTypeName(t.ContractTypeName),
            }).ToArray(),
        };

        if (topology.Connectors.Count > 0)
        {
            model["connectors"] = topology.Connectors.Select(c => new Dictionary<string, object>
            {
                ["name"] = c.Name,
                ["transport"] = c.Transport.DaprType,
                ["usedBy"] = c.UsedBy,
            }).ToArray();
        }

        if (topology.Services.Count > 0)
        {
            model["services"] = topology.Services.Select(s =>
            {
                var entry = new Dictionary<string, object> { ["name"] = s.Name };
                if (s.Service is ContainerService container)
                {
                    entry["image"] = container.Image;
                    entry["port"] = container.Port;
                }

                return entry;
            }).ToArray();
        }

        return model;
    }

    private static Dictionary<string, object> DisplayComponent(ComponentModel component)
    {
        var entry = new Dictionary<string, object>
        {
            ["name"] = component.Name,
            ["kind"] = KebabCase(component.Kind.ToString()),
        };

        if (component.Schedule is not null)
        {
            entry["schedule"] = component.Schedule;
        }

        if (component.Subscribes.Count > 0)
        {
            entry["subscribes"] = component.Subscribes
                .Select(t => new Dictionary<string, object> { ["pubsub"] = t.PubSubName, ["topic"] = t.TopicName })
                .ToArray();
        }

        if (component.Publishes.Count > 0)
        {
            entry["publishes"] = component.Publishes.Select(p =>
            {
                var edge = new Dictionary<string, object> { ["pubsub"] = p.PubSubName, ["topic"] = p.TopicName };
                if (p.Port != "default")
                {
                    edge["port"] = p.Port;
                }

                return edge;
            }).ToArray();
        }

        if (component.Connectors.Count > 0)
        {
            entry["connectors"] = component.Connectors
                .Select(c => new Dictionary<string, object>
                {
                    ["name"] = c.ConnectorName,
                    ["direction"] = c.Direction == ConnectorDirection.In ? "in" : "out",
                })
                .ToArray();
        }

        return entry;
    }

    private static string ShortTypeName(string fullName)
    {
        var index = fullName.LastIndexOfAny(['.', '+']);
        return index >= 0 ? fullName[(index + 1)..] : fullName;
    }

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
        var directory = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-')) ?? "./out";

        var discovered = SystemDiscovery.Discover(assembly);
        var artifacts = TopologyGenerator.Generate(discovered.Topology);
        artifacts.WriteTo(directory);

        Console.WriteLine($"generated {artifacts.Files.Count} files to '{directory}':");
        foreach (var file in artifacts.Files)
        {
            Console.WriteLine($"  {file.RelativePath}");
        }

        return 0;
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'. Expected: check | graph | generate.");
        return 2;
    }
}
