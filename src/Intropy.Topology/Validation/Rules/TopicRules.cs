using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// A component must not publish the same message twice. Publishing several distinct messages —
/// to one topic or several — is legal; repeating one is a redundant edge and almost certainly a
/// declaration slip.
/// </summary>
internal sealed class DuplicatePublishRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var component in topology.Components)
        {
            var duplicates = component.Publishes
                .GroupBy(p => p.Message, StringComparer.Ordinal)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicates)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"The message '{group.Key}' is published more than once by the same component.",
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
/// A subscription's messages must travel on one channel: the subscription is the component's
/// subscription to that channel. Materialization takes the first message's channel, so the
/// conflict is visible only in the raw declarations.
/// </summary>
internal sealed class SubscriptionChannelConflictRule : IDeclarationRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemBuilder builder)
    {
        foreach (var component in builder.Components)
        {
            foreach (var subscription in component.SubscriptionCalls)
            {
                var channels = subscription.Messages
                    .Select(m => (m.PubSubName, m.TopicName))
                    .Distinct()
                    .ToList();
                if (channels.Count <= 1)
                {
                    continue;
                }

                var described = string.Join(", ", channels
                    .Select(c => $"'{c.TopicName}' on pubsub '{c.PubSubName}'")
                    .Order(StringComparer.Ordinal));
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"A subscription handles messages from different channels ({described}); a subscription is to one channel, so all of its messages must travel on it.",
                    component.Name);
            }
        }
    }
}

/// <summary>A component must not handle the same message twice: each message is one route.</summary>
internal sealed class DuplicateHandledMessageRule : IDeclarationRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemBuilder builder)
    {
        foreach (var component in builder.Components)
        {
            var duplicates = component.SubscribeCalls
                .GroupBy(m => m.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .Order(StringComparer.Ordinal);

            foreach (var message in duplicates)
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"The message '{message}' is handled more than once by the same component.",
                    component.Name);
            }
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
