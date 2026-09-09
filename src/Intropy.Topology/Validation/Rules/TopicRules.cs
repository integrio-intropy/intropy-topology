using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// A component must not declare the same channel (pubsub, topic) twice on the
/// publish side. Publishing several distinct messages is legal; repeating one is
/// a redundant edge and almost certainly a declaration slip.
/// </summary>
internal sealed class DuplicatePublishRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var component in topology.Components)
        {
            var duplicates = component.Publishes
                .GroupBy(t => (t.PubSubName, t.TopicName))
                .Where(g => g.Count() > 1);

            foreach (var group in duplicates)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"The topic '{group.Key.TopicName}' on pubsub '{group.Key.PubSubName}' is published to more than once by the same component.",
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
/// One message identity must not resolve to two transport channels. Materialization is
/// first-seen-wins on the channel, so the conflict is visible only in the raw
/// <see cref="MessageRef"/> declarations.
/// </summary>
internal sealed class MessageChannelConflictRule : IDeclarationRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemBuilder builder)
    {
        var usages = builder.Components.SelectMany(MessageDeclarationUsages.Of);

        var conflicts = usages
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .Where(g => g.Select(m => (m.PubSubName, m.TopicName)).Distinct().Count() > 1);

        foreach (var group in conflicts)
        {
            var channels = string.Join(
                ", ",
                group.Select(m => $"'{m.TopicName}' on pubsub '{m.PubSubName}'").Distinct().Order(StringComparer.Ordinal));
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"The message '{group.Key}' is declared on conflicting channels: {channels}.",
                group.Key);
        }
    }
}

/// <summary>
/// One message identity must not carry two different payload contract types. The model
/// carries only the contract's name and materialization is first-seen-wins, so the
/// conflict is visible only in the raw <see cref="MessageRef"/> declarations.
/// </summary>
internal sealed class MessageContractConflictRule : IDeclarationRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemBuilder builder)
    {
        var usages = builder.Components.SelectMany(MessageDeclarationUsages.Of);

        var conflicts = usages
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .Where(g => g.Select(m => m.ContractType).Distinct().Count() > 1);

        foreach (var group in conflicts)
        {
            var contracts = string.Join(
                ", ",
                group.Select(m => m.ContractType.FullName ?? m.ContractType.Name).Distinct().Order(StringComparer.Ordinal));
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"The message '{group.Key}' is declared with conflicting contract types: {contracts}.",
                group.Key);
        }
    }
}

/// <summary>
/// One transport channel must not be used with two different payload contract types
/// under different message names. The model carries only the contract's name and
/// materialization is first-seen-wins, so the conflict is visible only in the raw
/// <see cref="MessageRef"/> declarations.
/// </summary>
internal sealed class TopicContractConflictRule : IDeclarationRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemBuilder builder)
    {
        var usages = builder.Components.SelectMany(MessageDeclarationUsages.Of);

        var conflicts = usages
            .GroupBy(m => (m.PubSubName, m.TopicName))
            .Where(g => g.Select(m => m.ContractType).Distinct().Count() > 1);

        foreach (var group in conflicts)
        {
            var contracts = string.Join(
                ", ",
                group.Select(m => m.ContractType.FullName ?? m.ContractType.Name).Distinct().Order(StringComparer.Ordinal));
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"The topic '{group.Key.TopicName}' on pubsub '{group.Key.PubSubName}' is declared with conflicting contract types: {contracts}.",
                group.Key.TopicName);
        }
    }
}

/// <summary>The raw <see cref="MessageRef"/> declarations of one component, publish side first.</summary>
file static class MessageDeclarationUsages
{
    public static IEnumerable<MessageRef> Of(Component component)
    {
        foreach (var message in component.PublishCalls)
        {
            yield return message;
        }

        foreach (var message in component.SubscribeCalls)
        {
            yield return message;
        }
    }
}
