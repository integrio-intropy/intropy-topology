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
/// Identifies a platform service by the Dapr app ID callers use. The service materializes
/// from component usage; this reference neither owns nor deploys its provider.
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
/// A connector — the named connection point between the system and the outside world.
/// Every edge block reaches the outside world through a connector. Its declared transport is
/// the deployed shape (value-free; connection values are environment-owned deployment
/// configuration); local F5 runs substitute their own resolution via the development
/// definition. The Dapr binding component's name is always derived
/// (<c>binding.&lt;connector-name&gt;</c>), never declared. Connectors are declared in the
/// SystemHost (typically a scaffolded <c>Connectors.cs</c>); they are system-owned and never
/// shared across systems. Direction is not part of the identity — it follows from usage
/// (<c>From</c> / <c>To</c>).
/// </summary>
public sealed record ConnectorRef
{
    /// <summary>The connector's name (DNS-1123 label, e.g. <c>pim</c>).</summary>
    public string Name { get; }

    /// <summary>The deployed transport shape — the kind of Dapr binding the connector materializes as after deployment.</summary>
    public Transport Transport { get; }

    private ConnectorRef(string name, Transport transport)
    {
        Name = name;
        Transport = transport;
    }

    /// <summary>Declares a connector with its deployed transport shape.</summary>
    /// <param name="name">The connector's name (DNS-1123 label).</param>
    /// <param name="transport">The value-free deployed Dapr binding transport shape.</param>
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
[JsonDerivedType(typeof(SftpTransport), "sftp")]
[JsonDerivedType(typeof(DefaultTransport), "default")]
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
    /// SFTP transport (<c>bindings.sftp</c>) for deployed environments. Its address,
    /// credentials, and path are supplied by deployment configuration.
    /// </summary>
    public static Transport Sftp() => new SftpTransport();

    /// <summary>
    /// Placeholder transport for scaffolded connectors whose deployed shape is not yet
    /// chosen. Compiles and passes capability checks so a freshly scaffolded system builds,
    /// runs locally, and generates local artifacts (local runs resolve every connector to
    /// localstorage regardless of transport); <c>task check</c> warns about it. Deployment
    /// generation must refuse it — replace with a concrete transport (e.g. <see cref="Sftp"/>)
    /// before deployment.
    /// </summary>
    public static Transport Default() => new DefaultTransport();
}

/// <summary>
/// Value-free SFTP transport: a Dapr binding component of type <c>bindings.sftp</c>.
/// Deployment configuration supplies its address, credentials, and path.
/// </summary>
public sealed record SftpTransport : Transport
{
    /// <inheritdoc />
    public override string DaprType => "bindings.sftp";

    /// <inheritdoc />
    [JsonIgnore]
    public override bool SupportsInput => true;

    /// <inheritdoc />
    [JsonIgnore]
    public override bool SupportsOutput => true;
}

/// <summary>
/// Placeholder transport: no real Dapr binding type. A scaffolded connector starts here
/// so the system compiles and runs locally immediately — local runs resolve every
/// connector to localstorage, so the declared transport is never consulted. <c>task
/// check</c> warns about it, and deployment generation must refuse to emit a component
/// for it. The developer replaces it with a concrete transport (e.g. <see cref="SftpTransport"/>)
/// before deployment.
/// </summary>
public sealed record DefaultTransport : Transport
{
    /// <inheritdoc />
    public override string DaprType => "";

    /// <inheritdoc />
    [JsonIgnore]
    public override bool SupportsInput => true;

    /// <inheritdoc />
    [JsonIgnore]
    public override bool SupportsOutput => true;
}
