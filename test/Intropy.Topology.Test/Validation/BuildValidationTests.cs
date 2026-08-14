namespace Intropy.Topology.Test.Validation;

public class BuildValidationTests
{
    private static SystemBuilder SystemWithThreeViolations()
    {
        var s = SystemBuilder.Create("test-system");
        // Extractor with no Publishes; duplicate component name; published topic nobody
        // subscribes to.
        s.AddExtractor("dup").From(TestPorts.Pim);
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
        Assert.Contains(exception.Diagnostics, d => d.Message.Contains("must be unique"));
        Assert.Contains(exception.Diagnostics, d => d.Message.Contains("must publish"));
        Assert.Contains(exception.Diagnostics, d => d.Message.Contains("no component subscribes"));
    }

    [Fact]
    public void Build_WithViolations_ShouldListEveryDiagnosticInMessage()
    {
        // Arrange
        var s = SystemWithThreeViolations();

        // Act
        var exception = Assert.Throws<TopologyValidationException>(() => s.Build());

        // Assert
        Assert.Contains("[Error]", exception.Message);
        Assert.Contains("must be unique", exception.Message);
        Assert.Contains("must publish", exception.Message);
        Assert.Contains("no component subscribes", exception.Message);
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
        // Arrange: a valid system plus a topic nobody subscribes to yet — an unconsumed
        // topic is a warning (the consumer may simply not exist), so the build succeeds.
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddExtractor("extra-source").Publishes(TestTopics.Enriched);

        // Act
        var ok = s.TryBuild(out var topology, out var diagnostics);

        // Assert
        Assert.True(ok);
        Assert.NotNull(topology);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("no component subscribes"));
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
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
