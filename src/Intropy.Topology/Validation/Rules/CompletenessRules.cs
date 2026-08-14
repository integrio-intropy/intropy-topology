using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// Block kinds with a required output must declare it — extractors must publish.
/// The block builders prevent illegal calls but cannot force missing ones. A loader's
/// destination may stay a private local component, so a loader without a <c>To</c> call
/// is legal.
/// </summary>
internal sealed class MissingRequiredOutputRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var component in topology.Components)
        {
            var missing = component.Kind switch
            {
                ComponentKind.Extractor when component.Publishes.Count == 0 =>
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
internal sealed class MissingRequiredSubscriptionRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var component in topology.Components)
        {
            var count = component.Subscribes.Count;
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
/// A transactional integration must declare both port directions: at least one
/// <c>From</c> (the receive side reads a source) and at least one <c>To</c> (the send
/// side writes a destination). Several ports per direction are legal — a run may
/// touch several sources or destinations — so the rule checks presence, not count.
/// The builder exposes both methods but cannot force the calls.
/// </summary>
internal sealed class MissingRequiredPortRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var component in topology.Components)
        {
            if (component.Kind is not ComponentKind.TransactionalIntegration)
            {
                continue;
            }

            if (!component.Ports.Any(c => c.Direction is PortDirection.In))
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    "Transactional integration components must read from at least one port; add a From call.",
                    component.Name);
            }

            if (!component.Ports.Any(c => c.Direction is PortDirection.Out))
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    "Transactional integration components must write to at least one port; add a To call.",
                    component.Name);
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
internal sealed class UnconsumedTopicRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var topic in topology.Topics.Where(t => t.Subscribers.Count == 0))
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
internal sealed class UnproducedTopicRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        foreach (var topic in topology.Topics.Where(t => t.Publishers.Count == 0))
        {
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Warning,
                $"The topic '{topic.TopicName}' on pubsub '{topic.PubSubName}' is subscribed to but no component publishes it.",
                topic.TopicName);
        }
    }
}
