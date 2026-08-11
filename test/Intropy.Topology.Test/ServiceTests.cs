namespace Intropy.Topology.Test;

public sealed class ServiceTests
{
    private static readonly TopicRef<string> s_topic = TopicRef<string>.Define("orders", "created");
    private static readonly ServiceRef s_idempotency = ServiceRef.Define("idempotency-service");

    [Fact]
    public void Build_WithServiceUsage_ShouldMaterializeOrderedConsumerEdges()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").Publishes(s_topic).Uses(s_idempotency);
        builder.AddLoader("loader").Subscribes(s_topic).Uses(s_idempotency);

        // Act
        var topology = builder.Build();

        // Assert
        Assert.Equal(["idempotency-service"], topology.Components[0].Uses);
        var service = Assert.Single(topology.Services);
        Assert.Equal("idempotency-service", service.AppId);
        Assert.Equal(["extractor", "loader"], service.Consumers);
    }

    [Fact]
    public void Validate_WithRepeatedServiceUsage_ShouldReportAnError()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").Publishes(s_topic).Uses(s_idempotency).Uses(s_idempotency);
        builder.AddLoader("loader").Subscribes(s_topic);

        // Act
        var diagnostics = builder.Validate();

        // Assert
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithRepeatedServiceUsageAndAppIdCollision_ShouldReportBothErrors()
    {
        // Arrange: the duplicate-usage and collision halves are separate rules that must compose
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("idempotency-service").Publishes(s_topic).Uses(s_idempotency).Uses(s_idempotency);
        builder.AddLoader("loader").Subscribes(s_topic);

        // Act
        var diagnostics = builder.Validate();

        // Assert
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("more than once", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("collides", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithComponentAppIdMatchingService_ShouldReportAnError()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("idempotency-service").Publishes(s_topic).Uses(s_idempotency);
        builder.AddLoader("loader").Subscribes(s_topic);

        // Act
        var diagnostics = builder.Validate();

        // Assert
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("collides", StringComparison.Ordinal));
    }
}
