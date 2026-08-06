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
        var connectors = new Dictionary<string, ConnectorAccumulator>();
        var services = new Dictionary<string, ServiceAccumulator>(StringComparer.Ordinal);

        foreach (var component in builder.Components)
        {
            components.Add(MaterializeComponent(component, topics, connectors, services));
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
                    ContractTypeName = t.Value.ContractTypeName,
                    Publishers = [.. t.Value.Publishers],
                    Subscribers = [.. t.Value.Subscribers],
                })
                .ToArray(),
            Connectors = connectors
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => new ConnectorResource
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
        };
    }

    private static ComponentModel MaterializeComponent(
        Component component,
        Dictionary<(string PubSub, string Topic), TopicAccumulator> topics,
        Dictionary<string, ConnectorAccumulator> connectors,
        Dictionary<string, ServiceAccumulator> services)
    {
        var subscribes = new List<TopicSubscription>();
        foreach (var topic in component.SubscribeCalls)
        {
            subscribes.Add(new TopicSubscription
            {
                PubSubName = topic.PubSubName,
                TopicName = topic.TopicName,
            });
            AccumulateTopic(topics, topic).Subscribers.Add(component.Name);
        }

        var publishes = new List<PublishEdge>();
        foreach (var (port, topic) in component.PublishCalls)
        {
            publishes.Add(new PublishEdge
            {
                Port = port,
                PubSubName = topic.PubSubName,
                TopicName = topic.TopicName,
            });
            AccumulateTopic(topics, topic).Publishers.Add(component.Name);
        }

        var connectorEdges = new List<ConnectorEdge>();
        var seenConnectorEdges = new HashSet<(string, ConnectorDirection)>();
        foreach (var (connector, direction) in component.ConnectorCalls)
        {
            AccumulateConnector(connectors, connector, direction).UsedBy.Add(component.Name);
            if (seenConnectorEdges.Add((connector.Name, direction)))
            {
                connectorEdges.Add(new ConnectorEdge { ConnectorName = connector.Name, Direction = direction });
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
            Connectors = connectorEdges,
            Uses = serviceAppIds,
        };
    }

    private static TopicAccumulator AccumulateTopic(
        Dictionary<(string PubSub, string Topic), TopicAccumulator> topics,
        TopicRef topic)
    {
        var key = (topic.PubSubName, topic.TopicName);
        if (!topics.TryGetValue(key, out var accumulator))
        {
            accumulator = new TopicAccumulator(topic.ContractType.FullName ?? topic.ContractType.Name);
            topics[key] = accumulator;
        }

        return accumulator;
    }

    private static ConnectorAccumulator AccumulateConnector(
        Dictionary<string, ConnectorAccumulator> connectors,
        ConnectorRef connector,
        ConnectorDirection direction)
    {
        if (!connectors.TryGetValue(connector.Name, out var accumulator))
        {
            accumulator = new ConnectorAccumulator();
            connectors[connector.Name] = accumulator;
        }

        accumulator.Directions.Add(direction);
        return accumulator;
    }

    private sealed class TopicAccumulator(string contractTypeName)
    {
        public string ContractTypeName { get; } = contractTypeName;
        public SortedSet<string> Publishers { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Subscribers { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ServiceAccumulator
    {
        public SortedSet<string> Consumers { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ConnectorAccumulator
    {
        public HashSet<ConnectorDirection> Directions { get; } = [];
        public SortedSet<string> UsedBy { get; } = new(StringComparer.Ordinal);
    }
}
