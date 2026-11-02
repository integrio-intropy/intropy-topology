using System.Text.Json.Serialization;

namespace Intropy.Topology.Model;

/// <summary>A topic a component subscribes to, and the messages it handles from it.</summary>
public sealed record TopicSubscription
{
    /// <summary>The Dapr pubsub component name.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic name.</summary>
    public required string TopicName { get; init; }

    /// <summary>The names of the messages the component handles from the topic, in declaration
    /// order. A message's name is its CloudEvent type.</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];

    /// <summary>The content filter of each handled message that has one, by message name: a Dapr
    /// CEL expression its events must also match. The message's events it leaves out are unhandled.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyDictionary<string, string>? Conditions { get; init; }

    /// <summary>The content filter for <paramref name="messageName"/>, if it has one.</summary>
    public string? ConditionFor(string messageName) =>
        Conditions is not null && Conditions.TryGetValue(messageName, out var condition) ? condition : null;

    /// <summary>What happens to the topic's messages the component does not handle — including a
    /// handled message's events its content filter leaves out.</summary>
    public UnhandledMessages Unhandled { get; init; } = UnhandledMessages.DeadLetter;

    /// <summary>Set when the subscription delivers in batches (Dapr bulk subscribe). Null for one
    /// message at a time.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BulkSubscription? Bulk { get; init; }
}

/// <summary>How a bulk subscription batches its messages.</summary>
public sealed record BulkSubscription
{
    /// <summary>The most messages the sidecar collects into one delivery.</summary>
    public required int MaxMessages { get; init; }

    /// <summary>How long the sidecar waits to fill a delivery before sending what it has.</summary>
    public required TimeSpan MaxWait { get; init; }
}

/// <summary>A component publishing a message to a topic.</summary>
public sealed record PublishEdge
{
    /// <summary>The Dapr pubsub component name.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic name.</summary>
    public required string TopicName { get; init; }

    /// <summary>The name of the message published — its CloudEvent type. A component may publish
    /// several messages to one topic.</summary>
    public string Message { get; init; } = "";
}

/// <summary>A component using a port in one direction.</summary>
public sealed record PortEdge
{
    /// <summary>The port's name.</summary>
    public required string PortName { get; init; }

    /// <summary>The direction the port is used in.</summary>
    public required PortDirection Direction { get; init; }
}

/// <summary>A gateway serving an endpoint.</summary>
public sealed record EndpointEdge
{
    /// <summary>The endpoint's name.</summary>
    public required string EndpointName { get; init; }

    /// <summary>The path callers post to.</summary>
    public required string Path { get; init; }
}

/// <summary>
/// A transactional integration's internal receive-to-send queue. Not a user-declared topic: the
/// names are minted at materialization — the system's pubsub and a topic named after the
/// component — so nothing outside the component publishes or subscribes on it.
/// </summary>
public sealed record InternalQueue
{
    /// <summary>The Dapr pubsub component name backing the queue: the system's own,
    /// <c>&lt;system&gt;-pubsub</c>.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic the receive pipeline publishes and the send pipeline subscribes to,
    /// <c>internal-&lt;component&gt;</c>. Unique per component, because brokers keep queues apart by
    /// topic, not by Dapr component.</summary>
    public required string TopicName { get; init; }

    /// <summary>The internal queue's topic for <paramref name="componentName"/>.</summary>
    public static string TopicNameFor(string componentName) => $"internal-{componentName}";
}

/// <summary>
/// The state a processor keeps: two Dapr state components on one table — a plain one it reads and
/// writes through when a decision publishes nothing, and one configured with the transactional
/// outbox it commits through when a decision publishes, so the state write and the event succeed
/// or fail together. The outbox publishes to exactly one channel, which is why a processor
/// publishes on one channel only. The names are minted per component, so state is never shared
/// between components; deployment chooses the backing store.
/// </summary>
public sealed record ProcessorState
{
    /// <summary>The plain state component's name (<c>&lt;component&gt;-state</c>).</summary>
    public required string StateStoreName { get; init; }

    /// <summary>The outbox-enabled state component's name (<c>&lt;component&gt;-state-outbox</c>).</summary>
    public required string OutboxStateStoreName { get; init; }

    /// <summary>The channel the outbox publishes the processor's messages on: the one channel the
    /// processor publishes to; <see langword="null"/> while it declares no publication (which
    /// validation rejects).</summary>
    public required MessageChannel? Outbox { get; init; }

    /// <summary>The plain state component's name for <paramref name="componentName"/>.</summary>
    public static string StateStoreNameFor(string componentName) => $"{componentName}-state";

    /// <summary>The outbox-enabled state component's name for <paramref name="componentName"/>.</summary>
    public static string OutboxStateStoreNameFor(string componentName) => $"{componentName}-state-outbox";
}

/// <summary>
/// The state a component's Dapr workflows run on: one state component, marked as the sidecar's
/// actor state store, minted per component so no two components share workflow instances. The
/// sidecar finds it by that marking; the component never names it.
/// </summary>
public sealed record WorkflowState
{
    /// <summary>The workflow state component's name (<c>&lt;component&gt;-workflow-state</c>).</summary>
    public required string StateStoreName { get; init; }

    /// <summary>The workflow state component's name for <paramref name="componentName"/>.</summary>
    public static string StateStoreNameFor(string componentName) => $"{componentName}-workflow-state";
}

/// <summary>
/// The one pubsub and the one topic a component runs on — what the framework reads from
/// <c>INTROPY_PUBSUB_NAME</c> and <c>INTROPY_TOPIC_NAME</c>, and derives nothing of its own. The
/// single derivation site, so the local host and deployment tooling pass the same names.
/// </summary>
/// <param name="PubSubName">The internal queue's pubsub for a transactional integration; otherwise
/// the pubsub every channel the component subscribes to or publishes on shares. Null when it has
/// no channel, or channels on several pubsubs.</param>
/// <param name="TopicName">The internal queue's topic for a transactional integration; otherwise
/// the component's only channel's topic. Null when it has no channel, or several.</param>
public sealed record ComponentChannel(string? PubSubName, string? TopicName)
{
    /// <summary>The channel <paramref name="component"/> runs on.</summary>
    public static ComponentChannel For(ComponentModel component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.InternalQueue is { } queue)
        {
            return new(queue.PubSubName, queue.TopicName);
        }

        var channels = component.Subscribes.Select(s => (s.PubSubName, s.TopicName))
            .Concat(component.Publishes.Select(p => (p.PubSubName, p.TopicName)))
            .Distinct()
            .ToList();
        var pubSubs = channels.Select(c => c.PubSubName).Distinct().ToList();
        return new(
            pubSubs.Count == 1 ? pubSubs[0] : null,
            channels.Count == 1 ? channels[0].TopicName : null);
    }
}

/// <summary>
/// The names a component's Dapr <c>Subscription</c> is rendered with — the single derivation site,
/// so every backend (local generation, Aspire, deployment tooling) and the framework agree.
/// </summary>
public static class SubscriptionRouting
{
    /// <summary>The route the sidecar sends a channel's unhandled messages to; the component's
    /// <see cref="UnhandledMessages"/> decides whether it acknowledges them.</summary>
    public const string UnhandledPath = "/unhandled";

    /// <summary>The <c>Subscription</c> resource's name for <paramref name="componentName"/>.</summary>
    public static string ResourceNameFor(string componentName) => $"{componentName}-subscription";

    /// <summary>The route a handled message is delivered on: its name — its CloudEvent type — as a path.</summary>
    public static string PathFor(string messageName) => $"/{messageName}";

    /// <summary>The CEL rule that selects <paramref name="messageName"/>'s events: by type, and by
    /// <paramref name="condition"/>, the message's content filter, when it has one.</summary>
    public static string MatchFor(string messageName, string? condition = null) =>
        condition is null
            ? $"event.type == '{messageName}'"
            : $"event.type == '{messageName}' && ({condition})";
}
