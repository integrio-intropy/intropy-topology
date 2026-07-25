namespace Intropy.Topology.Test.Validation;

public class BuildValidationTests
{
    private static SystemBuilder SystemWithThreeViolations()
    {
        var s = SystemBuilder.Create("test-system");
        // ITP206: extractor with no Publishes; ITP001: duplicate name; ITP208: published
        // topic nobody subscribes.
        s.AddExtractor("dup").From(TestConnectors.Pim);
        s.AddExtractor("dup").Publishes(TestTopics.Raw);
        return s;
    }

    [Fact]
    public void Build_WithMultipleViolations_ShouldAggregateAllDiagnostics()
    {
        // Arrange
        var s = SystemWithThreeViolations();

        // Act
        var exception = Assert.Throws<TopologyValidationException>(() => s.Build());

        // Assert
        Assert.Contains(exception.Diagnostics, d => d.Code == "ITP001");
        Assert.Contains(exception.Diagnostics, d => d.Code == "ITP206");
        Assert.Contains(exception.Diagnostics, d => d.Code == "ITP208");
    }

    [Fact]
    public void Build_WithViolations_ShouldListEveryDiagnosticInMessage()
    {
        // Arrange
        var s = SystemWithThreeViolations();

        // Act
        var exception = Assert.Throws<TopologyValidationException>(() => s.Build());

        // Assert
        Assert.Contains("ITP001", exception.Message);
        Assert.Contains("ITP206", exception.Message);
        Assert.Contains("ITP208", exception.Message);
        Assert.Contains("dup", exception.Message);
    }

    [Fact]
    public void TryBuild_WithInvalidSystem_ShouldReturnFalseWithDiagnostics()
    {
        // Arrange
        var s = SystemWithThreeViolations();

        // Act
        var ok = s.TryBuild(out var topology, out var diagnostics);

        // Assert
        Assert.False(ok);
        Assert.Null(topology);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void TryBuild_WithValidSystemAndWarnings_ShouldReturnTrueWithWarnings()
    {
        // Arrange: a valid component plus an edgeless transactional integration (ITP302 warning)
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddTransactionalIntegration("idle");

        // Act
        var ok = s.TryBuild(out var topology, out var diagnostics);

        // Assert
        Assert.True(ok);
        Assert.NotNull(topology);
        Assert.Contains(diagnostics, d => d.Code == "ITP302" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Validate_WithInvalidSystem_ShouldNotThrow()
    {
        // Arrange
        var s = SystemWithThreeViolations();

        // Act & Assert
        Assert.NotEmpty(s.Validate());
    }
}
