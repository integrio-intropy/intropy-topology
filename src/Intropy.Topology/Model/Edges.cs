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

/// <summary>
/// A transactional integration's internal receive-to-send hop. Not a user-declared topic:
/// the names are minted from the component name at materialization, so nothing outside the
/// component can subscribe to or publish on it.
/// </summary>
public sealed record InternalQueue
{
    /// <summary>The Dapr pubsub component name backing the hop.</summary>
    public required string PubSubName { get; init; }

    /// <summary>The topic the receive pipeline publishes and the send pipeline subscribes to.</summary>
    public required string TopicName { get; init; }
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
