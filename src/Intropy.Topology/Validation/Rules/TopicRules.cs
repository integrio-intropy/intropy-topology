using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// A component must not publish more than one topic; multi-output components are not
/// supported.
/// </summary>
internal sealed class DuplicatePublishRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var component in topology.Components)
        {
            if (component.Publishes.Count > 1)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"The component declares {component.Publishes.Count} Publishes calls; a component publishes exactly one topic.",
                    component.Name);
            }
        }
    }
}

/// <summary>A component must not subscribe to the same topic more than once.</summary>
internal sealed class DuplicateSubscriptionRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var component in topology.Components)
        {
            var duplicates = component.Subscribes
                .GroupBy(t => (t.PubSubName, t.TopicName))
                .Where(g => g.Count() > 1);

            foreach (var group in duplicates)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"The topic '{group.Key.TopicName}' on pubsub '{group.Key.PubSubName}' is subscribed to more than once by the same component.",
                    component.Name);
            }
        }
    }
}

/// <summary>
/// One topic must not be used with two different event contract types. The model carries
/// only the contract's name and materialization is first-seen-wins, so the conflict is
/// visible only in the raw <see cref="TopicRef"/> declarations.
/// </summary>
internal sealed class TopicContractConflictRule : IDeclarationRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemBuilder builder)
    {
        var usages = builder.Components.SelectMany(TopicUsages);

        var conflicts = usages
            .GroupBy(t => (t.PubSubName, t.TopicName))
            .Where(g => g.Select(t => t.ContractType).Distinct().Count() > 1);

        foreach (var group in conflicts)
        {
            var contracts = string.Join(
                ", ",
                group.Select(t => t.ContractType.FullName ?? t.ContractType.Name).Distinct().Order(StringComparer.Ordinal));
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"The topic '{group.Key.TopicName}' on pubsub '{group.Key.PubSubName}' is declared with conflicting contract types: {contracts}.",
                group.Key.TopicName);
        }
    }

    private static IEnumerable<TopicRef> TopicUsages(Component component)
    {
        foreach (var topic in component.PublishCalls)
        {
            yield return topic;
        }

        foreach (var topic in component.SubscribeCalls)
        {
            yield return topic;
        }
    }
}
