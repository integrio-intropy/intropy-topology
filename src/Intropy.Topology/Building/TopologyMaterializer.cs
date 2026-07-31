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

        foreach (var component in builder.Components)
        {
            components.Add(MaterializeComponent(component, topics, connectors));
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
                    Transport = c.Value.Transport,
                    DeployedTransport = c.Value.DeployedTransport,
                    Directions = [.. c.Value.Directions.Order()],
                    UsedBy = [.. c.Value.UsedBy],
                })
                .ToArray(),
        };
    }

    private static ComponentModel MaterializeComponent(
        Component component,
        Dictionary<(string PubSub, string Topic), TopicAccumulator> topics,
        Dictionary<string, ConnectorAccumulator> connectors)
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

        return new ComponentModel
        {
            Name = component.Name,
            Kind = component.Kind,
            Schedule = component.ScheduleCall,
            Subscribes = subscribes,
            Publishes = publishes,
            Connectors = connectorEdges,
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
            // First-seen wins on identity; conflicting declarations are reported by validation.
            accumulator = new ConnectorAccumulator(connector.Transport, connector.DeployedTransport);
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

    private sealed class ConnectorAccumulator(Transport transport, Transport? deployedTransport)
    {
        public Transport Transport { get; } = transport;
        public Transport? DeployedTransport { get; } = deployedTransport;
        public HashSet<ConnectorDirection> Directions { get; } = [];
        public SortedSet<string> UsedBy { get; } = new(StringComparer.Ordinal);
    }
}
