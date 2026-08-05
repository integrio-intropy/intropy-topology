using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// Block kinds with a required output must declare it — extractors must publish.
/// The block builders prevent illegal calls but cannot force missing ones. A loader's
/// destination may stay a private local component, so a loader without a <c>To</c> call
/// is legal.
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
                yield return new TopologyDiagnostic(DiagnosticSeverity.Error, missing, component.Name);
            }
        }
    }
}

/// <summary>
/// Block kinds that consume topics must subscribe correctly — loaders to exactly
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
                yield return new TopologyDiagnostic(DiagnosticSeverity.Error, message, component.Name);
            }
        }
    }
}

/// <summary>
/// Every published topic should have a subscriber. A message published into the
/// system with no consumer is a broken contract, not a fan-out reserve — but the
/// consumer may simply not exist yet, so the rule warns and local runs proceed.
/// Deployment validation is where an unconsumed topic becomes an error.
/// </summary>
internal sealed class UnconsumedTopicRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var topic in context.Topology.Topics.Where(t => t.Subscribers.Count == 0))
        {
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Warning,
                $"The topic '{topic.TopicName}' on pubsub '{topic.PubSubName}' is published but no component subscribes to it.",
                topic.TopicName);
        }
    }
}

/// <summary>
/// Every subscribed topic should have a publisher. A subscription nothing publishes
/// to will never receive a message — but the publisher may simply not exist yet,
/// so the rule warns and local runs proceed. Deployment validation is where an
/// unproduced topic becomes an error.
/// </summary>
internal sealed class UnproducedTopicRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var topic in context.Topology.Topics.Where(t => t.Publishers.Count == 0))
        {
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Warning,
                $"The topic '{topic.TopicName}' on pubsub '{topic.PubSubName}' is subscribed to but no component publishes it.",
                topic.TopicName);
        }
    }
}
