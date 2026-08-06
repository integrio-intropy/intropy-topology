namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// A component must not publish more than one topic. Every <c>Publishes</c> call
/// records the single <c>default</c> output port; multi-output components (named ports)
/// are not part of the minimal model.
/// </summary>
internal sealed class DuplicatePortRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var component in context.Builder.Components)
        {
            if (component.PublishCalls.Count > 1)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"The component declares {component.PublishCalls.Count} Publishes calls; a component publishes exactly one topic.",
                    component.Name);
            }
        }
    }
}

/// <summary>A component must not subscribe to the same topic more than once.</summary>
internal sealed class DuplicateSubscriptionRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var component in context.Builder.Components)
        {
            var duplicates = component.SubscribeCalls
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

/// <summary>One topic must not be used with two different event contract types.</summary>
internal sealed class TopicContractConflictRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        var usages = context.Builder.Components.SelectMany(TopicUsages);

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
        foreach (var (_, topic) in component.PublishCalls)
        {
            yield return topic;
        }

        foreach (var topic in component.SubscribeCalls)
        {
            yield return topic;
        }
    }
}

