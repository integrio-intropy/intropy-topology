namespace Intropy.Topology.Test;

public class DiscoveryTests
{
    private sealed class ValidSystem : ISystemDefinition
    {
        public string SystemName => "sample-system";

        public void Define(SystemBuilder builder)
        {
            builder.AddExtractor("raw-extractor").From(TestPorts.Pim).Publishes(TestTopics.Raw);
            builder.AddLoader("enriched-loader").Subscribes(TestTopics.Raw).To(TestPorts.Erp);
        }
    }

    private sealed class OtherValidSystem : ISystemDefinition
    {
        public string SystemName => "other-system";

        public void Define(SystemBuilder builder) => builder.AddExtractor("x").From(TestPorts.Pim).Publishes(TestTopics.Raw);
    }

    // An extractor that publishes nothing is an invalid topology (fails validation at Build).
    private sealed class InvalidSystem : ISystemDefinition
    {
        public string SystemName => "bad-system";

        public void Define(SystemBuilder builder) => builder.AddExtractor("silent").From(TestPorts.Pim);
    }

    private sealed class NotASystem;

    [Fact]
    public void DiscoverFrom_WithSingleDefinition_ShouldReturnValidatedTopologyAndDefinition()
    {
        // Act
        var discovered = SystemDiscovery.DiscoverFrom("test", [typeof(NotASystem), typeof(ValidSystem)]);

        // Assert
        Assert.Equal("sample-system", discovered.Topology.SystemName);
        Assert.Equal(["raw-extractor", "enriched-loader"], discovered.Topology.Components.Select(c => c.Name));
        Assert.IsType<ValidSystem>(discovered.Definition);
    }

    [Fact]
    public void DiscoverFrom_WithNoDefinition_ShouldThrow()
    {
        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => SystemDiscovery.DiscoverFrom("test", [typeof(NotASystem)]));
        Assert.Contains("No ISystemDefinition", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverFrom_WithMultipleDefinitions_ShouldThrow()
    {
        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => SystemDiscovery.DiscoverFrom("test", [typeof(ValidSystem), typeof(OtherValidSystem)]));
        Assert.Contains("Multiple ISystemDefinition", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverFrom_WithInvalidTopology_ShouldThrowValidationException()
    {
        // Act / Assert
        Assert.Throws<TopologyValidationException>(
            () => SystemDiscovery.DiscoverFrom("test", [typeof(InvalidSystem)]));
    }
}
