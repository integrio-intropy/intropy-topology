using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Intropy.Topology.Validation;

namespace Intropy.Topology;

/// <summary>
/// Reference to a pub/sub topic: a Dapr pubsub component name plus a topic name.
/// Topics are declared as static fields — in a scaffolded <c>Topics.cs</c> for
/// system-internal topics, or in a shared contracts package for cross-system topics;
/// the declaration site is the visibility marker. The backing pubsub resource
/// materializes from usage; there is no <c>AddTopic</c> on the builder.
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
/// A <see cref="TopicRef"/> typed with its event contract <typeparamref name="T"/>.
/// The type parameter documents the contract at the declaration site and lets future
/// generation/codegen reflect on it; topology-level wiring only uses the names.
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
/// A connector — the named connection point between the system and the outside world.
/// Every edge block reaches the outside world through a connector. Its transport selects
/// the Dapr binding type it materializes as; the Dapr binding component's name is always
/// derived (<c>binding.&lt;connector-name&gt;</c>), never declared. Connectors are declared
/// in the SystemHost (typically a scaffolded <c>Connectors.cs</c>); they are system-owned and
/// never shared across systems. Direction is not part of the identity — it follows from
/// usage (<c>From</c> / <c>To</c>).
/// </summary>
public sealed record ConnectorRef
{
    /// <summary>The connector's name (DNS-1123 label, e.g. <c>pim</c>).</summary>
    public string Name { get; }

    /// <summary>The transport — the kind of Dapr binding the connector materializes as.</summary>
    public Transport Transport { get; }

    private ConnectorRef(string name, Transport transport)
    {
        Name = name;
        Transport = transport;
    }

    /// <summary>
    /// Declares a connector resolved to a transport — the form <c>intropy system create</c>
    /// emits for the default local-file resolution, where the transport's endpoint (e.g. a
    /// <see cref="FileTransport"/> folder) is the connection itself.
    /// </summary>
    /// <param name="name">The connector's name (DNS-1123 label).</param>
    /// <param name="transport">The kind of Dapr binding the connector materializes as.</param>
    /// <exception cref="ArgumentException">The name is not a valid DNS-1123 label.</exception>
    public static ConnectorRef Define([ConstantExpected] string name, Transport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return new ConnectorRef(NameRules.RequireLabel(name, nameof(name)), transport);
    }
}

/// <summary>
/// The kind of Dapr binding a connector materializes as. All connectivity goes through
/// Dapr — the transport selects the Dapr binding component's <c>spec.type</c>; it never
/// bypasses Dapr. The component name is always derived: <c>binding.&lt;connector-name&gt;</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$transport")]
[JsonDerivedType(typeof(FileTransport), "file")]
public abstract record Transport
{
    private protected Transport()
    {
    }

    /// <summary>The Dapr binding component's <c>spec.type</c> (e.g. <c>bindings.localstorage</c>).</summary>
    public abstract string DaprType { get; }

    /// <summary>
    /// Local file transport (<c>bindings.localstorage</c>): reads and writes files in a folder on
    /// the host. The folder is the endpoint. This is the default resolution
    /// <c>intropy system create</c> applies.
    /// </summary>
    /// <param name="rootPath">The folder the binding reads and writes, relative to the SystemHost directory or absolute.</param>
    /// <exception cref="ArgumentException">The root path is null or whitespace.</exception>
    public static Transport File([ConstantExpected] string rootPath) => new FileTransport(rootPath);
}

/// <summary>
/// Local file transport: a Dapr binding component of type <c>bindings.localstorage</c> rooted at
/// <see cref="RootPath"/>. The folder is the endpoint.
/// </summary>
public sealed record FileTransport : Transport
{
    /// <inheritdoc />
    public override string DaprType => "bindings.localstorage";

    /// <summary>The folder the binding reads and writes, relative to the SystemHost directory or absolute.</summary>
    public string RootPath { get; }

    /// <summary>Creates a local file transport. Prefer <see cref="Transport.File"/>.</summary>
    /// <param name="rootPath">The folder the binding reads and writes.</param>
    /// <exception cref="ArgumentException">The root path is null or whitespace.</exception>
    public FileTransport(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = rootPath;
    }
}
