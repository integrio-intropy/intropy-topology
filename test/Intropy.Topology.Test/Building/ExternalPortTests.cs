using System.Text.Json;
using Intropy.Topology.Model;

namespace Intropy.Topology.Test.Building;

public class ExternalPortTests
{
    private static readonly PortRef s_campaigns = PortRef.DefineExternal("campaigns");
    private static readonly MessageRef<string> s_changed = MessageRef<string>.Define("campaign-changed");

    private static SystemTopology Build(Action<SystemBuilder> define)
    {
        var builder = SystemBuilder.Create("campaign-sync");
        define(builder);
        return builder.Build();
    }

    [Fact]
    public void DefineExternal_ShouldDeclareExternalKind()
    {
        // Act & Assert
        Assert.Equal(PortKind.External, s_campaigns.Kind);
        Assert.Equal(PortKind.Binding, PortRef.Define("erp").Kind);
    }

    [Fact]
    public void Build_WithExternalSource_ShouldMaterializeExternalPortAndCursorStore()
    {
        // Act
        var topology = Build(b =>
        {
            b.AddExtractor("campaign-extractor").From(s_campaigns).Publishes(s_changed);
            b.AddLoader("campaign-loader").Subscribes(s_changed);
        });

        // Assert
        var port = Assert.Single(topology.Ports);
        Assert.Equal(PortKind.External, port.Kind);
        var extractor = topology.Components.Single(c => c.Name == "campaign-extractor");
        Assert.Equal("campaign-extractor-cursors", extractor.CursorStore);
        Assert.Null(topology.Components.Single(c => c.Name == "campaign-loader").CursorStore);
    }

    [Fact]
    public void Build_WithOnlyBindingPorts_ShouldSerializeWithoutKindOrCursorStore()
    {
        // Arrange
        var topology = Build(b =>
        {
            b.AddExtractor("x").From(PortRef.Define("erp")).Publishes(s_changed);
            b.AddLoader("y").Subscribes(s_changed);
        });

        // Act
        var json = JsonSerializer.Serialize(topology);

        // Assert — systems without external ports serialize as before.
        Assert.DoesNotContain("\"Kind\":\"Binding\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CursorStore", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithExternalPortWritten_ShouldMintNoCursorStoreForTheWriter()
    {
        // Act
        var topology = Build(b =>
        {
            b.AddExtractor("campaign-extractor").From(s_campaigns).Publishes(s_changed);
            b.AddLoader("campaign-loader").Subscribes(s_changed).To(s_campaigns);
        });

        // Assert — one external system, read by one component and written by another.
        var port = Assert.Single(topology.Ports);
        Assert.Equal(PortKind.External, port.Kind);
        Assert.Equal([PortDirection.In, PortDirection.Out], port.Directions);
        Assert.Equal("campaign-extractor-cursors", topology.Components.Single(c => c.Name == "campaign-extractor").CursorStore);
        Assert.Null(topology.Components.Single(c => c.Name == "campaign-loader").CursorStore);
    }

    [Fact]
    public void Build_WithPortDeclaredWithTwoKinds_ShouldFail()
    {
        // Act
        var exception = Assert.Throws<TopologyValidationException>(() => Build(b =>
        {
            b.AddExtractor("a").From(s_campaigns).Publishes(s_changed);
            b.AddLoader("b").Subscribes(s_changed).To(PortRef.Define("campaigns"));
        }));

        // Assert
        Assert.Contains(exception.Diagnostics, d => d.Target == "campaigns" && d.Message.Contains("both as a binding port and as an external port", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WithCursorStoreNamedLikeAPort_ShouldFail()
    {
        // Act — component "x" mints "x-cursors", which a port already names.
        var exception = Assert.Throws<TopologyValidationException>(() => Build(b =>
        {
            b.AddExtractor("x").From(s_campaigns).Publishes(s_changed);
            b.AddLoader("y").Subscribes(s_changed).To(PortRef.Define("x-cursors"));
        }));

        // Assert
        Assert.Contains(exception.Diagnostics, d => d.Target == "x-cursors");
    }
}
