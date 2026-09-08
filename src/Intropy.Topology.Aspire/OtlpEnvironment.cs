using System.Diagnostics;
using Intropy.Topology.Model;

namespace Intropy.Topology.Aspire;

/// <summary>
/// Translates the declared OTLP settings into the standard <c>OTEL_EXPORTER_OTLP_*</c>
/// environment variables — the names both the .NET OpenTelemetry SDK and daprd read. The
/// model carries semantics, this composition owns the names; a future deployment backend
/// composes the same variables for containers. Header values pass through verbatim, so
/// environment placeholders such as <c>${OTLP_API_KEY}</c> survive to the runtime that
/// resolves them.
/// </summary>
internal static class OtlpEnvironment
{
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string ProtocolVariable = "OTEL_EXPORTER_OTLP_PROTOCOL";
    public const string HeadersVariable = "OTEL_EXPORTER_OTLP_HEADERS";

    public static IReadOnlyDictionary<string, string> For(OtlpSettings otlp)
    {
        ArgumentNullException.ThrowIfNull(otlp);
        var variables = new Dictionary<string, string>(3, StringComparer.Ordinal)
        {
            [EndpointVariable] = otlp.Endpoint.ToString(),
        };
        if (otlp.Headers.Count > 0)
        {
            variables[HeadersVariable] = string.Join(
                ",", otlp.Headers.Select(h => $"{h.Key}={h.Value}"));
        }

        var protocol = Protocol(otlp.Protocol);
        variables[ProtocolVariable] = protocol;

        return variables;
    }

    private static string Protocol(OtlpProtocol protocol) => protocol switch
    {
        OtlpProtocol.Grpc => "grpc",
        OtlpProtocol.HttpProtobuf => "http/protobuf",
        _ => throw new UnreachableException($"Unhandled OTLP protocol '{protocol}'."),
    };
}
