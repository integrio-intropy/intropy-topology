namespace Intropy.Topology.Test;

public sealed class ServiceTests
{
    private static readonly MessageRef<string> s_message = MessageRef<string>.Define("created", pubSub: "orders");
    private static readonly ServiceRef s_idempotency = ServiceRef.Define("idempotency-service");

    [Fact]
    public void Build_WithServiceUsage_ShouldMaterializeOrderedConsumerEdges()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").Publishes(s_message).Calls(s_idempotency);
        builder.AddLoader("loader").Subscribes(s_message).Calls(s_idempotency);

        // Act
        var topology = builder.Build();

        // Assert
        Assert.Equal(["idempotency-service"], topology.Components[0].Uses);
        var service = Assert.Single(topology.Services);
        Assert.Equal("idempotency-service", service.AppId);
        Assert.Equal(["extractor", "loader"], service.Consumers);
    }

    [Fact]
    public void Calls_DeclaredTwice_ShouldThrowAtTheDeclaration()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder
            .AddExtractor("extractor").Publishes(s_message)
            .Calls(s_idempotency).Calls(s_idempotency));
        Assert.Contains("extractor", exception.Message);
        Assert.Contains("idempotency-service", exception.Message);
    }

    [Fact]
    public void Validate_WithComponentAppIdMatchingService_ShouldReportAnError()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("idempotency-service").Publishes(s_message).Calls(s_idempotency);
        builder.AddLoader("loader").Subscribes(s_message);

        // Act
        var diagnostics = builder.Validate();

        // Assert
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("collides", StringComparison.Ordinal));
    }
}
