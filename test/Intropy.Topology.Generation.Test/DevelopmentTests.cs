using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation.Test;

public class DevelopmentBuilderTests
{
    private static readonly ConnectorRef s_connector = ConnectorRef.Define("erp");

    private static SystemTopology TopologyWithConnector()
    {
        var topic = TopicRef<string>.Define("orders", "created");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").From(s_connector).Publishes(topic);
        builder.AddLoader("loader").Subscribes(topic);
        return builder.Build();
    }

    [Fact]
    public void Build_WithAllConnectorsResolved_ShouldProduceAbsoluteRootPaths()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithConnector(), Directory.GetCurrentDirectory());
        builder.Files(s_connector).RootPath("./test/erp");

        // Act
        var manifest = builder.Build();

        // Assert
        var file = Assert.Single(manifest.Files);
        Assert.Equal("erp", file.ConnectorName);
        Assert.Equal(Path.GetFullPath("./test/erp"), file.RootPath);
        Assert.Empty(manifest.Mocks);
    }

    [Fact]
    public void Files_WithUnknownConnector_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithConnector(), Directory.GetCurrentDirectory());
        var unknown = ConnectorRef.Define("unknown");

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Files(unknown));
        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not used by the system topology", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_WithDuplicateResolution_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithConnector(), Directory.GetCurrentDirectory());
        builder.Files(s_connector).RootPath("./test/erp");

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Files(s_connector));
        Assert.Contains("more than one file resolution", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RootPath_WithSecondPath_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithConnector(), Directory.GetCurrentDirectory());
        var files = builder.Files(s_connector);
        files.RootPath("./test/erp");

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => files.RootPath("./test/other"));
        Assert.Contains("more than one root path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithUnresolvedConnector_ShouldThrow()
    {
        // Arrange: the topology uses 'erp' but no file resolution is declared
        var builder = new DevelopmentBuilder(TopologyWithConnector(), Directory.GetCurrentDirectory());

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Build());
        Assert.Contains("erp", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no local file resolution", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithPathEscapingSystemHost_ShouldThrow()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithConnector(), Directory.GetCurrentDirectory());
        builder.Files(s_connector).RootPath("../outside");

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Build());
        Assert.Contains("escapes the SystemHost directory", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithMissingRootPath_ShouldThrow()
    {
        // Arrange: Files() declared but RootPath never called — bypass the duplicate guard
        var topology = TopologyWithConnector();
        var builder = new DevelopmentBuilder(topology, Directory.GetCurrentDirectory());
        builder.Files(s_connector);

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => builder.Build());
        Assert.Contains("does not declare a root path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithRootPathEqualToSystemHost_ShouldResolve()
    {
        // Arrange
        var builder = new DevelopmentBuilder(TopologyWithConnector(), Directory.GetCurrentDirectory());
        builder.Files(s_connector).RootPath(".");

        // Act
        var manifest = builder.Build();

        // Assert
        Assert.Equal(Path.GetFullPath("."), Assert.Single(manifest.Files).RootPath);
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

            var builder = new DevelopmentBuilder(TopologyWithConnector(), host.FullName);
            builder.Files(s_connector).RootPath("./link");

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
