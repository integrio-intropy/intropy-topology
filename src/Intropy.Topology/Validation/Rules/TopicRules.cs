using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// A subscription that defaults to dead-lettering unhandled messages should know which
/// channel mates it leaves unhandled. The materialized topics carry every message named
/// on a channel <see cref="TopicResource.Messages"/>, so the set difference of that
/// vs. the messages a subscription handles is the complete picture for the choice.
/// </summary>
/// <remarks>
/// Dead-lettering is the default <see cref="UnhandledMessages"/>, so the quiet failure
/// mode is real: a loader handling one message on a channel carrying several dead-letters
/// the rest with no signal in its declaration. Each subscribing component gets its own
/// consumer group, so a message another subscriber handles is still delivered — and
/// dead-letters — here; the rule is per subscribing component, never about whether some
/// component handles the message. Content filters do not affect the rule: it works at
/// message granularity, and a <c>when</c> filter's filtered-out events are covered by
/// the same dead-letter choice. Severity is <see cref="DiagnosticSeverity.Warning"/>
/// because the unhandled message may be meant for a component not yet declared —
/// deployment validation is where the warning can become an error, as with one-sided
/// topics.
/// </remarks>
internal sealed class UnhandledChannelMessagesRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        var topicsByChannel = topology.Topics.ToDictionary(
            t => (t.PubSubName, t.TopicName));

        foreach (var component in topology.Components)
        {
            foreach (var subscription in component.Subscribes)
            {
                // IgnoreOthers() is the deliberate acknowledgment — nothing to warn about.
                if (subscription.Unhandled is not UnhandledMessages.DeadLetter ||
                    !topicsByChannel.TryGetValue(
                        (subscription.PubSubName, subscription.TopicName),
                        out var topic))
                {
                    continue;
                }

                var unhandled = topic.Messages
                    .Except(subscription.Messages, StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();

                if (unhandled.Length == 0)
                {
                    continue;
                }

                var names = string.Join(", ", unhandled.Select(n => $"'{n}'"));
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Component '{component.Name}' handles {subscription.Messages.Count} of {topic.Messages.Count} messages on topic '{topic.TopicName}' (pubsub '{topic.PubSubName}'); {names} will dead-letter for this component — handle it with AlsoHandles(...), or declare IgnoreOthers() to acknowledge and drop unhandled messages.",
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
