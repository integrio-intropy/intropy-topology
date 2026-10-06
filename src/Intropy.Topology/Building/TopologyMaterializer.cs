using Intropy.Topology.Model;

namespace Intropy.Topology.Building;

/// <summary>
/// Folds the builder declarations into the immutable model: components in declaration
/// order, resources deduplicated from usage and sorted for determinism. Materialization
/// never fails — structurally broken declarations still materialize (first-seen wins on
/// conflicts) so validation rules can inspect and report the full picture.
/// </summary>
internal static class TopologyMaterializer
{
    public static SystemTopology Materialize(SystemBuilder builder)
    {
        var components = new List<ComponentModel>();
        var topics = new Dictionary<(string PubSub, string Topic), TopicAccumulator>();
        var messages = new Dictionary<string, MessageAccumulator>(StringComparer.Ordinal);
        var ports = new Dictionary<string, PortAccumulator>();
        var services = new Dictionary<string, ServiceAccumulator>(StringComparer.Ordinal);

        foreach (var component in builder.Components)
        {
            components.Add(MaterializeComponent(component, topics, messages, ports, services));
        }

        return new SystemTopology
        {
            SystemName = builder.SystemName,
            Components = components,
            Topics = topics
                .OrderBy(t => t.Key.PubSub, StringComparer.Ordinal)
                .ThenBy(t => t.Key.Topic, StringComparer.Ordinal)
                .Select(t => new TopicResource
                {
                    PubSubName = t.Key.PubSub,
                    TopicName = t.Key.Topic,
                    Messages = [.. t.Value.Messages],
                    Publishers = [.. t.Value.Publishers],
                    Subscribers = [.. t.Value.Subscribers],
                })
                .ToArray(),
            MessageGroups =
            [
                new MessageGroupResource
                {
                    Name = builder.SystemName,
                    Messages = messages
                        .OrderBy(m => m.Key, StringComparer.Ordinal)
                        .Select(m => new MessageResource
                        {
                            Name = m.Key,
                            ContractTypeName = m.Value.ContractTypeName,
                            Channel = new MessageChannel
                            {
                                PubSubName = m.Value.PubSubName,
                                TopicName = m.Value.TopicName,
                            },
                            Publishers = [.. m.Value.Publishers],
                            Subscribers = [.. m.Value.Subscribers],
                        })
                        .ToArray(),
                },
            ],
            Ports = ports
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => new PortResource
                {
                    Name = c.Key,
                    Directions = [.. c.Value.Directions.Order()],
                    UsedBy = [.. c.Value.UsedBy],
                })
                .ToArray(),
            Services = services
                .OrderBy(s => s.Key, StringComparer.Ordinal)
                .Select(s => new ServiceResource { AppId = s.Key, Consumers = [.. s.Value.Consumers] })
                .ToArray(),
            Otlp = builder.OtlpDeclaration,
        };
    }

    private static ComponentModel MaterializeComponent(
        Component component,
        Dictionary<(string PubSub, string Topic), TopicAccumulator> topics,
        Dictionary<string, MessageAccumulator> messages,
        Dictionary<string, PortAccumulator> ports,
        Dictionary<string, ServiceAccumulator> services)
    {
        var subscribes = new List<TopicSubscription>();
        foreach (var subscription in component.SubscriptionCalls)
        {
            // A subscription's channel is its first message's; the declaration site enforces
            // that every handled message travels on that one channel.
            var channel = subscription.Messages[0];
            subscribes.Add(new TopicSubscription
            {
                PubSubName = channel.PubSubName,
                TopicName = channel.TopicName,
                Messages = [.. subscription.Messages.Select(m => m.Name).Distinct(StringComparer.Ordinal)],
                Conditions = subscription.Conditions.Count == 0
                    ? null
                    : new Dictionary<string, string>(subscription.Conditions, StringComparer.Ordinal),
                Unhandled = subscription.Unhandled,
                Bulk = (component as LoaderComponent)?.Bulk,
            });

            foreach (var message in subscription.Messages)
            {
                AccumulateTopic(topics, message).Subscribers.Add(component.Name);
                AccumulateMessage(messages, message).Subscribers.Add(component.Name);
            }
        }

        var publishes = new List<PublishEdge>();
        foreach (var message in component.PublishCalls)
        {
            publishes.Add(new PublishEdge
            {
                PubSubName = message.PubSubName,
                TopicName = message.TopicName,
                Message = message.Name,
            });
            AccumulateTopic(topics, message).Publishers.Add(component.Name);
            AccumulateMessage(messages, message).Publishers.Add(component.Name);
        }

        var portEdges = new List<PortEdge>();
        var seenPortEdges = new HashSet<(string, PortDirection)>();
        foreach (var (port, direction) in component.PortCalls)
        {
            AccumulatePort(ports, port, direction).UsedBy.Add(component.Name);
            if (seenPortEdges.Add((port.Name, direction)))
            {
                portEdges.Add(new PortEdge { PortName = port.Name, Direction = direction });
            }
        }

        var serviceAppIds = new List<string>();
        var seenServices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in component.ServiceCalls)
        {
            if (!services.TryGetValue(service.AppId, out var accumulator))
            {
                services[service.AppId] = accumulator = new ServiceAccumulator();
            }

            accumulator.Consumers.Add(component.Name);
            if (seenServices.Add(service.AppId))
            {
                serviceAppIds.Add(service.AppId);
            }
        }

        return new ComponentModel
        {
            Name = component.Name,
            Kind = component.Kind,
            Subscribes = subscribes,
            Publishes = publishes,
            Ports = portEdges,
            Uses = serviceAppIds,
            InternalQueue = component.Kind is ComponentKind.TransactionalIntegration
                ? new InternalQueue
                {
                    PubSubName = $"internal-{component.Name}",
                    TopicName = "hop",
                }
                : null,
        };
    }

    private static TopicAccumulator AccumulateTopic(
        Dictionary<(string PubSub, string Topic), TopicAccumulator> topics,
        MessageRef message)
    {
        var key = (message.PubSubName, message.TopicName);
        if (!topics.TryGetValue(key, out var accumulator))
        {
            accumulator = new TopicAccumulator();
            topics[key] = accumulator;
        }

        accumulator.Messages.Add(message.Name);
        return accumulator;
    }

    // First-seen-wins on channel/contract conflicts: materialization never fails, and
    // the declaration rules report the conflicts against the raw MessageRef usages.
    private static MessageAccumulator AccumulateMessage(
        Dictionary<string, MessageAccumulator> messages,
        MessageRef message)
    {
        if (!messages.TryGetValue(message.Name, out var accumulator))
        {
            accumulator = new MessageAccumulator(
                message.ContractType.FullName ?? message.ContractType.Name,
                message.PubSubName,
                message.TopicName);
            messages[message.Name] = accumulator;
        }

        return accumulator;
    }

    private static PortAccumulator AccumulatePort(
        Dictionary<string, PortAccumulator> ports,
        PortRef port,
        PortDirection direction)
    {
        if (!ports.TryGetValue(port.Name, out var accumulator))
        {
            accumulator = new PortAccumulator();
            ports[port.Name] = accumulator;
        }

        accumulator.Directions.Add(direction);
        return accumulator;
    }

    private sealed class TopicAccumulator
    {
        public SortedSet<string> Messages { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Publishers { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Subscribers { get; } = new(StringComparer.Ordinal);
    }

    private sealed class MessageAccumulator(string contractTypeName, string pubSubName, string topicName)
    {
        public string ContractTypeName { get; } = contractTypeName;
        public string PubSubName { get; } = pubSubName;
        public string TopicName { get; } = topicName;
        public SortedSet<string> Publishers { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Subscribers { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ServiceAccumulator
    {
        public SortedSet<string> Consumers { get; } = new(StringComparer.Ordinal);
    }

    private sealed class PortAccumulator
    {
        public HashSet<PortDirection> Directions { get; } = [];
        public SortedSet<string> UsedBy { get; } = new(StringComparer.Ordinal);
    }
}
