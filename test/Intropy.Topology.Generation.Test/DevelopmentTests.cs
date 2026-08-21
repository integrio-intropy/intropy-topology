using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation.Test;

public class DevelopmentBuilderTests
{
    private static readonly PortRef s_port = PortRef.Define("erp");

    private static SystemTopology TopologyWithPort()
    {
        var topic = TopicRef<string>.Define("orders", "created");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").From(s_port).Publishes(topic);
        builder.AddLoader("loader").Subscribes(topic);
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
}
