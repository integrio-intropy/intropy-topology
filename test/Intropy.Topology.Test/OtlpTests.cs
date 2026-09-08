using System.Text.Json;
using Intropy.Topology.Model;

namespace Intropy.Topology.Test;

/// <summary>
/// The OTLP sink declaration: system-scoped, eagerly validated, and materialized verbatim.
/// The default path — no declaration — serializes and materializes exactly as before this
/// feature existed, which the byte-exact snapshot in EndToEndTests guards.
/// </summary>
public class OtlpTests
{
    private static SystemTopology Build(Action<SystemBuilder> configure)
    {
        var builder = SystemBuilder.Create("telemetry");
        configure(builder);
        builder.AddLoader("telemetry-loader").Subscribes(TelemetryFlow.Events);
        return builder.Build();
    }

    private static class TelemetryFlow
    {
        public sealed record Payload(string Id);

        public static readonly TopicRef<Payload> Events =
            TopicRef<Payload>.Define("telemetry-pubsub", "telemetry-events");
    }

    [Fact]
    public void Build_WithoutOtlp_ShouldLeaveTheDefaultSinkUnset()
    {
        // Act
        var topology = Build(_ => { });

        // Assert
        Assert.Null(topology.Otlp);
    }

    [Fact]
    public void Otlp_WithEndpointOnly_ShouldMaterializeGrpcDefaults()
    {
        // Act
        var topology = Build(builder => builder.Otlp("http://collector:4317"));

        // Assert
        var otlp = Assert.IsType<OtlpSettings>(topology.Otlp);
        Assert.Equal("http://collector:4317", otlp.Endpoint);
        Assert.Equal(OtlpProtocol.Grpc, otlp.Protocol);
        Assert.Empty(otlp.Headers);
    }

    [Fact]
    public void Otlp_WithProtocolAndHeaders_ShouldMaterializeAllSettings()
    {
        // Act
        var topology = Build(builder => builder
            .Otlp("https://collector.internal:4318")
            .WithProtocol(OtlpProtocol.HttpProtobuf)
            .WithHeader("x-api-key", "${OTLP_API_KEY}")
            .WithHeader("x-tenant", "acme"));

        // Assert
        var otlp = topology.Otlp!;
        Assert.Equal(OtlpProtocol.HttpProtobuf, otlp.Protocol);
        Assert.Equal(
            [("x-api-key", "${OTLP_API_KEY}"), ("x-tenant", "acme")],
            otlp.Headers.Select(h => (h.Key, h.Value)));
    }

    [Fact]
    public void Otlp_WithRepeatHeaderName_ShouldReplaceTheValue()
    {
        // Act
        var topology = Build(builder => builder
            .Otlp("http://collector:4317")
            .WithHeader("x-api-key", "first")
            .WithHeader("x-api-key", "second"));

        // Assert
        Assert.Equal([("x-api-key", "second")], topology.Otlp!.Headers.Select(h => (h.Key, h.Value)));
    }

    [Fact]
    public void Otlp_WithSecondDeclaration_ShouldThrowInsteadOfReplacing()
    {
        // Arrange
        var builder = SystemBuilder.Create("telemetry");
        builder.Otlp("http://collector:4317");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Otlp("http://other:4317"));
        Assert.Contains("telemetry", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://collector:4317")]
    public void Otlp_WithNonHttpAbsoluteEndpoint_ShouldThrowEagerly(string endpoint)
    {
        // Arrange
        var builder = SystemBuilder.Create("telemetry");

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => builder.Otlp(endpoint));
        Assert.Equal("endpoint", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x api-key")]
    public void WithHeader_WithInvalidName_ShouldThrowEagerly(string name)
    {
        // Arrange
        var builder = SystemBuilder.Create("telemetry");
        var otlp = builder.Otlp("http://collector:4317");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => otlp.WithHeader(name, "value"));
    }

    [Fact]
    public void WithHeader_WithControlCharacterInValue_ShouldThrowEagerly()
    {
        // Arrange
        var builder = SystemBuilder.Create("telemetry");
        var otlp = builder.Otlp("http://collector:4317");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => otlp.WithHeader("x-api-key", "broken\nvalue"));
    }

    [Fact]
    public void Serialize_WithoutOtlp_ShouldNotEmitTheProperty()
    {
        // The null sink is absent from serialization, so saved models from before the
        // declaration existed are byte-identical with models from after it.
        var json = JsonSerializer.Serialize(Build(_ => { }));

        Assert.DoesNotContain($"\"{nameof(SystemTopology.Otlp)}\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_WithOtlp_ShouldRoundTripTheDeclaration()
    {
        // Arrange
        var topology = Build(builder => builder
            .Otlp("http://collector:4317")
            .WithProtocol(OtlpProtocol.HttpProtobuf)
            .WithHeader("x-api-key", "${OTLP_API_KEY}"));

        // Act
        var json = JsonSerializer.Serialize(topology);
        var deserialized = JsonSerializer.Deserialize<SystemTopology>(json);

        // Assert
        var otlp = deserialized!.Otlp;
        Assert.NotNull(otlp);
        Assert.Equal("http://collector:4317", otlp.Endpoint);
        Assert.Equal(OtlpProtocol.HttpProtobuf, otlp.Protocol);
        Assert.Equal([("x-api-key", "${OTLP_API_KEY}")], otlp.Headers.Select(h => (h.Key, h.Value)));
    }
}
