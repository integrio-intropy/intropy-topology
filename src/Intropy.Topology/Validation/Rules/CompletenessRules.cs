using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// ITP206: block kinds with a required output must declare it — extractors must publish.
/// The block builders prevent illegal calls but cannot force missing ones. A loader's
/// destination may stay a private local component, so a loader without a <c>To</c> call
/// is legal (see decision 0008).
/// </summary>
internal sealed class MissingRequiredOutputRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var component in context.Builder.Components)
        {
            var missing = component.Kind switch
            {
                ComponentKind.Extractor when component.PublishCalls.Count == 0 =>
                    "Extractor components must publish to at least one topic; add a Publishes call.",
                _ => null,
            };

            if (missing is not null)
            {
                yield return new TopologyDiagnostic("ITP206", DiagnosticSeverity.Error, missing, component.Name);
            }
        }
    }
}

/// <summary>
/// ITP207: block kinds that consume topics must subscribe correctly — loaders to exactly
/// one topic. The block builders prevent illegal calls but cannot force a required
/// subscription.
/// </summary>
internal sealed class MissingRequiredSubscriptionRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var component in context.Builder.Components)
        {
            var count = component.SubscribeCalls.Count;
            var message = component.Kind switch
            {
                ComponentKind.Loader when count != 1 =>
                    "Loader components must subscribe to exactly one topic.",
                _ => null,
            };

            if (message is not null)
            {
                yield return new TopologyDiagnostic("ITP207", DiagnosticSeverity.Error, message, component.Name);
            }
        }
    }
}

/// <summary>
/// ITP208: every published topic must have a subscriber. A message published into the
/// system with no consumer is a broken contract, not a fan-out reserve (see decision 0008).
/// </summary>
internal sealed class UnconsumedTopicRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var topic in context.Topology.Topics.Where(t => t.Subscribers.Count == 0))
        {
            yield return new TopologyDiagnostic(
                "ITP208",
                DiagnosticSeverity.Error,
                $"The topic '{topic.TopicName}' on pubsub '{topic.PubSubName}' is published but no component subscribes to it.",
                topic.TopicName);
        }
    }
}

/// <summary>
/// ITP209: every subscribed topic must have a publisher. A subscription nothing publishes
/// to will never receive a message (see decision 0008).
/// </summary>
internal sealed class UnproducedTopicRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var topic in context.Topology.Topics.Where(t => t.Publishers.Count == 0))
        {
            yield return new TopologyDiagnostic(
                "ITP209",
                DiagnosticSeverity.Error,
                $"The topic '{topic.TopicName}' on pubsub '{topic.PubSubName}' is subscribed to but no component publishes it.",
                topic.TopicName);
        }
    }
}
