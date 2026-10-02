using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation.Test;

public class DevelopmentBuilderTests
{
    private static readonly PortRef s_port = PortRef.Define("erp");

    private static SystemTopology TopologyWithPort()
    {
        var message = MessageRef<string>.Define("created", "orders");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").From(s_port).Publishes(message);
        builder.AddLoader("loader").Subscribes(message);
        return builder.Build();
    }

    [Fact]
    public void Build_WithAllPortsResolved_ShouldProduceRelativeRootPaths()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        builder.Files(s_port).RootPath("./test/erp");

        // Act
        var manifest = builder.Build();

        // Assert
        var file = Assert.Single(manifest.Files);
        Assert.Equal("erp", file.PortName);
        Assert.Equal(Path.Combine("test", "erp"), file.RootPath);
        Assert.Empty(manifest.Mocks);
    }

    [Fact]
    public void Files_WithUnknownPort_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        var unknown = PortRef.Define("unknown");

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Files(unknown));
        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not used by the system topology", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_WithDuplicateResolution_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        builder.Files(s_port).RootPath("./test/erp");

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Files(s_port));
        Assert.Contains("more than one file resolution", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RootPath_WithSecondPath_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        var files = builder.Files(s_port);
        files.RootPath("./test/erp");

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => files.RootPath("./test/other"));
        Assert.Contains("more than one root path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithUnresolvedPort_ShouldThrow()
    {
        // Arrange: the topology uses 'erp' but no file resolution is declared
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Build());
        Assert.Contains("erp", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no local file resolution", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithPathEscapingSystemHost_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        builder.Files(s_port).RootPath("../outside");

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Build());
        Assert.Contains("escapes the SystemHost directory", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithMissingRootPath_ShouldThrow()
    {
        // Arrange: Files() declared but RootPath never called — bypass the duplicate guard
        var topology = TopologyWithPort();
        var builder = new DevelopmentBuilder(topology, Directory.GetCurrentDirectory());
        builder.Files(s_port);

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Build());
        Assert.Contains("does not declare a root path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithRootPathEqualToSystemHost_ShouldResolve()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        builder.Files(s_port).RootPath(".");

        // Act
        var manifest = builder.Build();

        // Assert
        Assert.Equal(".", Assert.Single(manifest.Files).RootPath);
    }

    [Fact]
    public void Build_WithSymlinkedRootPathEscapingSystemHost_ShouldThrow()
    {
        // Arrange: a symlink inside the SystemHost pointing outside it
        var workspace = Directory.CreateTempSubdirectory("intropy-devtest-");
        try
        {
            var outside = Directory.CreateDirectory(Path.Combine(workspace.FullName, "outside"));
            var host = Directory.CreateDirectory(Path.Combine(workspace.FullName, "host"));
            Directory.CreateSymbolicLink(Path.Combine(host.FullName, "link"), outside.FullName);

            var builder = new DevelopmentBuilder(TopologyWithPort(), host.FullName);
            builder.Files(s_port).RootPath("./link");

            // Act & Assert
            var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Build());
            Assert.Contains("escapes the SystemHost directory", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public void Build_WithRerun_ShouldProduceManifestEntry()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        builder.Files(s_port).RootPath("./test/erp");
        builder.Rerun("extractor").Every(TimeSpan.FromSeconds(30));

        // Act
        var manifest = builder.Build();

        // Assert
        var rerun = Assert.Single(manifest.Reruns);
        Assert.Equal("extractor", rerun.ComponentName);
        Assert.Equal(TimeSpan.FromSeconds(30), rerun.Delay);
    }

    [Fact]
    public void Rerun_WithUnknownComponent_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Rerun("ghost"));
        Assert.Contains("ghost", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not declared by the system topology", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rerun_WithResidentComponent_ShouldThrow()
    {
        // Arrange — a loader stays resident on its subscription; there is no run to repeat.
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Rerun("loader"));
        Assert.Contains("loader", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stays resident", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rerun_WithDuplicateDeclaration_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        builder.Rerun("extractor").Every(TimeSpan.FromSeconds(30));

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Rerun("extractor"));
        Assert.Contains("more than one re-run declaration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_WithSecondDelay_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        var rerun = builder.Rerun("extractor");
        rerun.Every(TimeSpan.FromSeconds(30));

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => rerun.Every(TimeSpan.FromMinutes(5)));
        Assert.Contains("more than one re-run delay", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Every_WithNonPositiveDelay_ShouldThrow(int seconds)
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(
            () => builder.Rerun("extractor").Every(TimeSpan.FromSeconds(seconds)));
        Assert.Contains("must be positive", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithRerunMissingDelay_ShouldThrow()
    {
        // Arrange: Rerun() declared but Every() never called
        var builder = new DevelopmentBuilder(TopologyWithPort(), Directory.GetCurrentDirectory());
        builder.Files(s_port).RootPath("./test/erp");
        builder.Rerun("extractor");

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Build());
        Assert.Contains("extractor", exception.Message, StringComparison.Ordinal);
        Assert.Contains("do not declare a delay", exception.Message, StringComparison.Ordinal);
    }
}
