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

/// <summary>
/// Reference to a shared platform service the system depends on (e.g. an idempotency
/// store). Services are declared as static fields in a scaffolded <c>Services.cs</c> and
/// registered on the system with <c>UsesService</c> — unlike topics and connectors they do
/// not materialize from usage, because no edge references them. The run backend starts the
/// service and starts every component only after it is ready; no connectivity is injected —
/// starting the service and the startup ordering is the whole contract.
/// </summary>
public sealed record ServiceRef
{
    /// <summary>The service's name (DNS-1123 label; becomes the run backend's resource name).</summary>
    public string Name { get; }

    /// <summary>What the service materializes as.</summary>
    public Service Service { get; }

    private ServiceRef(string name, Service service)
    {
        Name = name;
        Service = service;
    }

    /// <summary>Declares a shared platform service.</summary>
    /// <param name="name">The service's name (DNS-1123 label).</param>
    /// <param name="service">What the service materializes as.</param>
    /// <exception cref="ArgumentException">The name is not a valid DNS-1123 label.</exception>
    public static ServiceRef Define([ConstantExpected] string name, Service service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new ServiceRef(NameRules.RequireLabel(name, nameof(name)), service);
    }
}

/// <summary>
/// What a shared platform service materializes as when the system runs. Mirrors
/// <see cref="Transport"/>: a closed, JSON-discriminated hierarchy of service kinds.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$service")]
[JsonDerivedType(typeof(ContainerService), "container")]
public abstract record Service
{
    private protected Service()
    {
    }

    /// <summary>
    /// An arbitrary container listening on a fixed port. The image may embed a tag
    /// (<c>redis:7</c>); without one the backend's default (<c>latest</c>) applies.
    /// Digest (<c>@sha256:…</c>) references are not supported. The declared port is both
    /// the container port and the fixed host port it is published on.
    /// </summary>
    /// <param name="image">The container image reference, optionally with a tag.</param>
    /// <param name="port">The port the service listens on (container port == host port).</param>
    /// <exception cref="ArgumentException">The image is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The port is outside 1–65535.</exception>
    public static Service Container([ConstantExpected] string image, int port) =>
        new ContainerService(image, port);
}

/// <summary>
/// A platform service running as a container on a fixed port. The port is both the
/// container port and the host port — generation runs before the system starts, so
/// endpoints must be known up front.
/// </summary>
public sealed record ContainerService : Service
{
    /// <summary>The container image reference, optionally with a tag; digests unsupported.</summary>
    public string Image { get; }

    /// <summary>The port the service listens on (container port == fixed host port).</summary>
    public int Port { get; }

    /// <summary>Creates a container service. Prefer <see cref="Service.Container"/>.</summary>
    /// <param name="image">The container image reference, optionally with a tag.</param>
    /// <param name="port">The port the service listens on.</param>
    /// <exception cref="ArgumentException">The image is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The port is outside 1–65535.</exception>
    public ContainerService(string image, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        Image = image;
        Port = port;
    }
}
