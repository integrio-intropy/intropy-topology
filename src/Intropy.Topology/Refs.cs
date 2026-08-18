using System.Diagnostics.CodeAnalysis;
using Intropy.Topology.Validation;

namespace Intropy.Topology;

/// <summary>
/// Identifies a pub/sub topic by its Dapr component and topic names. The backing
/// resource materializes only when a component uses the reference.
/// </summary>
public abstract record TopicRef
{
    /// <summary>The Dapr pubsub component name (DNS-1123 subdomain).</summary>
    public string PubSubName { get; }

    /// <summary>The topic name within the pubsub (DNS-1123 subdomain).</summary>
    public string TopicName { get; }

    /// <summary>The event contract type carried on the topic.</summary>
    public abstract Type ContractType { get; }

    private protected TopicRef(string pubSubName, string topicName)
    {
        PubSubName = NameRules.RequireSubdomain(pubSubName, nameof(pubSubName));
        TopicName = NameRules.RequireSubdomain(topicName, nameof(topicName));
    }
}

/// <summary>
/// A <see cref="TopicRef"/> whose generic argument identifies the event contract. Topology
/// wiring uses the topic names, while <see cref="TopicRef.ContractType"/> exposes the type.
/// </summary>
/// <typeparam name="T">The event contract type published on the topic.</typeparam>
public sealed record TopicRef<T> : TopicRef
{
    /// <inheritdoc />
    public override Type ContractType => typeof(T);

    private TopicRef(string pubSubName, string topicName)
        : base(pubSubName, topicName)
    {
    }

    /// <summary>Declares a topic on a pubsub component.</summary>
    /// <param name="pubSubName">The Dapr pubsub component name (DNS-1123 subdomain).</param>
    /// <param name="topicName">The topic name (DNS-1123 subdomain).</param>
    /// <exception cref="ArgumentException">A name is not a valid DNS-1123 subdomain.</exception>
    public static TopicRef<T> Define(
        [ConstantExpected] string pubSubName,
        [ConstantExpected] string topicName) =>
        new(pubSubName, topicName);
}

/// <summary>
/// Identifies a platform service by the Dapr app ID callers use. The app ID is a minted
/// identity: deployment honors it rather than maintaining its own copy. It is unqualified —
/// Dapr service resolution in a cluster is namespace-scoped, so the identity holds within
/// the system's own namespace. The service materializes from component usage; this reference
/// neither owns nor deploys its provider.
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

