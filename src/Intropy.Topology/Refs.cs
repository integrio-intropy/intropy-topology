using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
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
/// Identifies a system-owned connection point and the Dapr binding transport used to
/// materialize it. Direction follows from <c>From</c> and <c>To</c> usage; the binding
/// component name is derived as <c>binding.&lt;connector-name&gt;</c>.
/// </summary>
public sealed record ConnectorRef
{
    /// <summary>The connector's name (DNS-1123 label, e.g. <c>pim</c>).</summary>
    public string Name { get; }

    /// <summary>The local transport — the kind of Dapr binding the connector materializes as during F5 runs.</summary>
    public Transport Transport { get; }

    /// <summary>
    /// The transport shape used after deployment, or <see langword="null"/> when the local
    /// transport also applies to deployed environments.
    /// </summary>
    public Transport? DeployedTransport { get; init; }

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

    /// <summary>
    /// Declares the value-free transport shape that materializes for deployment. Connection
    /// values remain environment-owned deployment configuration; local F5 runs keep using
    /// <see cref="Transport"/>.
    /// </summary>
    /// <param name="transport">The deployed Dapr binding transport shape.</param>
    /// <returns>A connector reference with the deployed transport set.</returns>
    public ConnectorRef WithDeployed(Transport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return this with { DeployedTransport = transport };
    }
}

/// <summary>
/// The kind of Dapr binding a connector materializes as. All connectivity goes through
/// Dapr — the transport selects the Dapr binding component's <c>spec.type</c>; it never
/// bypasses Dapr. The component name is always derived: <c>binding.&lt;connector-name&gt;</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$transport")]
[JsonDerivedType(typeof(FileTransport), "file")]
[JsonDerivedType(typeof(SftpTransport), "sftp")]
public abstract record Transport
{
    private protected Transport()
    {
    }

    /// <summary>The Dapr binding component's <c>spec.type</c> (e.g. <c>bindings.localstorage</c>).</summary>
    public abstract string DaprType { get; }

    /// <summary>Whether the transport can provide input to a component through <c>From</c>.</summary>
    [JsonIgnore]
    public abstract bool SupportsInput { get; }

    /// <summary>Whether the transport can accept component output through <c>To</c>.</summary>
    [JsonIgnore]
    public abstract bool SupportsOutput { get; }

    /// <summary>
    /// Local file transport (<c>bindings.localstorage</c>): reads and writes files in a folder on
    /// the host. The folder is the endpoint. This is the default resolution
    /// <c>intropy system create</c> applies.
    /// </summary>
    /// <param name="rootPath">The folder the binding reads and writes, relative to the SystemHost directory or absolute.</param>
    /// <exception cref="ArgumentException">The root path is null or whitespace.</exception>
    public static Transport File([ConstantExpected] string rootPath) => new FileTransport(rootPath);

    /// <summary>
    /// SFTP transport (<c>bindings.sftp</c>) for deployed environments. Its address,
    /// credentials, and path are supplied by deployment configuration.
    /// </summary>
    public static Transport Sftp() => new SftpTransport();
}

/// <summary>
/// Local file transport: a Dapr binding component of type <c>bindings.localstorage</c> rooted at
/// <see cref="RootPath"/>. The folder is the endpoint.
/// </summary>
public sealed record FileTransport : Transport
{
    /// <inheritdoc />
    public override string DaprType => "bindings.localstorage";

    /// <inheritdoc />
    [JsonIgnore]
    public override bool SupportsInput => true;

    /// <inheritdoc />
    [JsonIgnore]
    public override bool SupportsOutput => true;

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
/// Value-free SFTP output transport: a Dapr binding component of type <c>bindings.sftp</c>.
/// Deployment configuration supplies its address, credentials, and path.
/// </summary>
public sealed record SftpTransport : Transport
{
    /// <inheritdoc />
    public override string DaprType => "bindings.sftp";

    /// <inheritdoc />
    [JsonIgnore]
    public override bool SupportsInput => false;

    /// <inheritdoc />
    [JsonIgnore]
    public override bool SupportsOutput => true;
}
