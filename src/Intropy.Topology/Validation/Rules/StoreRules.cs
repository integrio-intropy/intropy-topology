using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// A store is a Dapr Component in the system's namespace, like a pubsub, a port's binding and a
/// cursor store, so its minted name must not equal any of them.
/// </summary>
internal sealed class StoreNameCollisionRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        var taken = topology.Topics.Select(t => t.PubSubName)
            .Concat(topology.Components.Select(c => c.InternalQueue?.PubSubName).OfType<string>())
            .Concat(topology.Ports.Select(p => p.DaprComponentName))
            .Concat(topology.Components.Select(c => c.CursorStore).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var store in (topology.Stores ?? []).Where(s => taken.Contains(s.Name)))
        {
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"The {(store.Role is StoreRole.Claim ? "claim" : "archive")} store '{store.Name}' minted for the system has the same name as a pubsub, a port or a cursor store; all become Dapr Component names in the same namespace.",
                store.Name);
        }
    }
}

/// <summary>
/// A component that claim-checks publishes pointers into the claim store. A subscriber of the same
/// topic that does not claim-check has no access to the store, so it cannot read the payload a
/// pointer names: warn, naming both ends.
/// </summary>
internal sealed class ClaimCheckSubscriberRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        // A set, not a lookup by name: rules run on systems that may declare a name twice.
        var claimChecking = topology.Components.Where(c => c.ClaimStore is not null).Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var topic in topology.Topics)
        {
            var claiming = topic.Publishers.Where(claimChecking.Contains).ToArray();
            if (claiming.Length == 0)
            {
                continue;
            }

            foreach (var subscriber in topic.Subscribers.Where(s => !claimChecking.Contains(s)))
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Component '{subscriber}' subscribes to topic '{topic.TopicName}', which '{string.Join("', '", claiming)}' publishes with a claim check, but does not declare ClaimChecks(): it cannot read a payload kept in the claim store.",
                    subscriber);
            }
        }
    }
}
