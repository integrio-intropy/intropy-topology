using System.Diagnostics.CodeAnalysis;
using Intropy.Topology.Validation;

namespace Intropy.Topology;

/// <summary>
/// Identifies a message by its logical name and the transport channel it moves over
/// (the Dapr pubsub component and topic names). A message that names no pubsub travels on
/// the system's own, <c>&lt;system&gt;-pubsub</c>, resolved when the topology is built. A single message identity implies all
/// three: the name, the channel, and the payload contract type — the topic is a
/// derivation of the message, never an independent declaration. The channel and
/// contract materialize only when a component uses the reference.
/// </summary>
public abstract record MessageRef
{
    /// <summary>The message's logical name (DNS-1123 subdomain).</summary>
    public string Name { get; }

    /// <summary>The Dapr pubsub component name carrying the message (DNS-1123 subdomain), or
    /// null for the system's own pubsub, which the materialized model names
    /// <c>&lt;system&gt;-pubsub</c>.</summary>
    public string? PubSubName { get; }

    /// <summary>The topic name the message flows over within the pubsub (DNS-1123 subdomain).</summary>
    public string TopicName { get; }

    /// <summary>The payload contract type transported by the message.</summary>
    public abstract Type ContractType { get; }

    private protected MessageRef(string name, string? pubSubName, string topicName)
    {
        Name = NameRules.RequireSubdomain(name, nameof(name));
        PubSubName = pubSubName is null ? null : NameRules.RequireSubdomain(pubSubName, nameof(pubSubName));
        TopicName = NameRules.RequireSubdomain(topicName, nameof(topicName));
    }

    /// <summary>The channel for diagnostics: the topic and the pubsub it travels on.</summary>
    internal string DescribeChannel() => PubSubName is null
        ? $"'{TopicName}' on the system's pubsub"
        : $"'{TopicName}' on pubsub '{PubSubName}'";
}

/// <summary>
/// A <see cref="MessageRef"/> whose generic argument identifies the payload contract.
/// Component wiring uses the message name and channel, while
/// <see cref="MessageRef.ContractType"/> exposes the contract type.
/// </summary>
/// <typeparam name="T">The payload contract type transported by the message.</typeparam>
public sealed record MessageRef<T> : MessageRef
{
    /// <inheritdoc />
    public override Type ContractType => typeof(T);

    private MessageRef(string name, string? pubSubName, string topicName)
        : base(name, pubSubName, topicName)
    {
    }

    /// <summary>Declares a message; the channel defaults to the system's pubsub,
    /// <c>&lt;system&gt;-pubsub</c>, and the topic <c>{name}</c>. Declare
    /// <paramref name="pubSub"/> and/or <paramref name="topic"/> to override — for example when
    /// the logical identity differs from the transport topic.</summary>
    /// <param name="name">The message's logical name (DNS-1123 subdomain).</param>
    /// <param name="pubSub">The Dapr pubsub component name (DNS-1123 subdomain); defaults to
    /// the system's own, <c>&lt;system&gt;-pubsub</c>. Every system in an environment namespace
    /// owns its own pubsub component, so name another only to reach a component this system
    /// does not own.</param>
    /// <param name="topic">The topic name within the pubsub (DNS-1123 subdomain);
    /// defaults to <paramref name="name"/>.</param>
    /// <exception cref="ArgumentException">A name (message, pubsub, or topic) is not a
    /// valid DNS-1123 subdomain.</exception>
    public static MessageRef<T> Define(
        [ConstantExpected] string name,
        [ConstantExpected] string? pubSub = null,
        [ConstantExpected] string? topic = null) =>
        new(name, pubSub, topic ?? name);
}

/// <summary>
/// Identifies a platform service by the Dapr app ID callers use. The app ID is a minted
/// identity: deployment honors it rather than maintaining its own copy. It is unqualified —
/// Dapr service resolution in a cluster is namespace-scoped, so the identity holds within
/// the system's own namespace. The service materializes when a component declares a call to
/// it; this reference neither owns nor deploys its provider.
/// </summary>
public sealed record ServiceRef
{
    /// <summary>The service's Dapr app ID (DNS-1123 label).</summary>
    public string AppId { get; }

    private ServiceRef(string appId) => AppId = NameRules.RequireLabel(appId, nameof(appId));

    /// <summary>Declares a reusable external platform-service identity.</summary>
    /// <param name="appId">The Dapr app ID callers invoke.</param>
    /// <exception cref="ArgumentException">The app ID is not a valid DNS-1123 label.</exception>
    public static ServiceRef Define([ConstantExpected] string appId) => new(appId);
}

/// <summary>
/// A port — the named connection point between the system and the outside world.
/// Every edge block reaches the outside world through a port. The name is the whole
/// identity: the Dapr binding component takes the port's name unchanged, never
/// declared separately, and the binding's deployed
/// <c>spec.type</c> (with its address and credentials) is environment-owned deployment
/// configuration the topology deliberately does not repeat. Local F5 runs substitute their
/// own resolution via the development definition. Ports are declared in the SystemHost
/// (typically a scaffolded <c>Ports.cs</c>); they are system-owned and never shared
/// across systems. Direction is not part of the identity — it follows from usage
/// (<c>From</c> / <c>To</c>).
/// </summary>
public sealed record PortRef
{
    /// <summary>The port's name (DNS-1123 label, e.g. <c>pim</c>).</summary>
    public string Name { get; }

    private PortRef(string name) => Name = name;

    /// <summary>Declares a port.</summary>
    /// <param name="name">The port's name (DNS-1123 label).</param>
    /// <exception cref="ArgumentException">The name is not a valid DNS-1123 label.</exception>
    public static PortRef Define([ConstantExpected] string name) =>
        new(NameRules.RequireLabel(name, nameof(name)));
}


/// <summary>
/// An endpoint — the named HTTP interface callers push items into the system through. A gateway
/// serves it itself, so unlike a <see cref="PortRef"/> it has no Dapr binding and no local file
/// resolution. The name is its identity and shares the ports' namespace, since both name a
/// resource of the system; the path is where callers <c>POST</c>, and the ingress in front of the
/// gateway routes it. Endpoints are declared in the SystemHost (typically a scaffolded
/// <c>Endpoints.cs</c>); they are system-owned and never shared across systems.
/// </summary>
public sealed record EndpointRef
{
    /// <summary>The endpoint's name (DNS-1123 label, e.g. <c>order-gateway-endpoint</c>).</summary>
    public string Name { get; }

    /// <summary>The path callers post to, starting with <c>/</c> (e.g. <c>/orders</c>).</summary>
    public string Path { get; }

    private EndpointRef(string name, string path)
    {
        Name = name;
        Path = path;
    }

    /// <summary>Declares an endpoint.</summary>
    /// <param name="name">The endpoint's name (DNS-1123 label).</param>
    /// <param name="path">The path callers post to: <c>/</c> followed by ASCII letters, digits,
    /// <c>/</c>, <c>.</c>, <c>_</c> and <c>-</c>.</param>
    /// <exception cref="ArgumentException">The name is not a valid DNS-1123 label, or the path is
    /// not a valid endpoint path.</exception>
    public static EndpointRef Define([ConstantExpected] string name, [ConstantExpected] string path) =>
        new(NameRules.RequireLabel(name, nameof(name)), NameRules.RequirePath(path, nameof(path)));
}
