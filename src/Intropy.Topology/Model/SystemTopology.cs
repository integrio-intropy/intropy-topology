using System.Text.Json.Serialization;

namespace Intropy.Topology.Model;

/// <summary>
/// The immutable, validated topology of one integration system: its components (in
/// declaration order) and the resources materialized from their usage (sorted).
/// Plain serializable data — generation tooling consumes this model.
/// </summary>
public sealed record SystemTopology
{
    /// <summary>The system's name (DNS-1123 label).</summary>
    public required string SystemName { get; init; }

    /// <summary>All components, in declaration order.</summary>
    public required IReadOnlyList<ComponentModel> Components { get; init; }

    /// <summary>Topics materialized from publish and subscribe edges.</summary>
    public required IReadOnlyList<TopicResource> Topics { get; init; }

    /// <summary>Ports materialized from port usage.</summary>
    public required IReadOnlyList<PortResource> Ports { get; init; }

    /// <summary>External platform services materialized from component usage.</summary>
    public required IReadOnlyList<ServiceResource> Services { get; init; }

    /// <summary>
    /// The OTLP export target declared for the system, or <see langword="null"/> when telemetry
    /// goes to each runtime's default sink. Null is deliberately omitted when serializing so a
    /// topology that declares nothing serializes exactly as it did before OTLP was declarable —
    /// a topology property that always appears (as <c>null</c>) would change every saved model.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OtlpSettings? Otlp { get; init; }
}

/// <summary>The OTLP wire protocol a system exports telemetry with.</summary>
public enum OtlpProtocol
{
    /// <summary>OTLP over gRPC — the protocol's conventional port is 4317.</summary>
    Grpc,

    /// <summary>OTLP over HTTP with protobuf payloads — the protocol's conventional port is 4318.</summary>
    HttpProtobuf,
}

/// <summary>
/// The OTLP export configuration for one system: a base endpoint, the wire protocol, and
/// headers. The declaration is semantic — runtimes translate it into the standard
/// <c>OTEL_EXPORTER_OTLP_*</c> environment variables — so the model never owns variable
/// names or transport encoding.
/// </summary>
public sealed record OtlpSettings
{
    /// <summary>
    /// The OTLP base endpoint, exactly as declared — not canonicalized, so every consumer
    /// composes the same string and the declared sink never drifts.
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>The OTLP wire protocol; gRPC unless declared otherwise.</summary>
    public OtlpProtocol Protocol { get; init; } = OtlpProtocol.Grpc;

    /// <summary>HTTP headers sent with every export request; empty when none declared.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
