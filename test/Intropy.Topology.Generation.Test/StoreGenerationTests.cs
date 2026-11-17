using System.Text.Json;
using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation.Test;

public class StoreGenerationTests
{
    private static readonly PortRef s_erp = PortRef.Define("erp");
    private static readonly PortRef s_wms = PortRef.Define("wms");

    private static SystemTopology Topology()
    {
        var builder = SystemBuilder.Create("orders");
        builder.AddTransactionalIntegration("order-sync").From(s_erp).To(s_wms).ClaimChecks().Archives();
        builder.AddTransactionalIntegration("stock-sync").From(s_erp).To(s_wms);
        return builder.Build();
    }

    private static DevelopmentManifest Development(SystemTopology topology)
    {
        var development = new DevelopmentBuilder(topology, Directory.GetCurrentDirectory());
        development.Files(s_erp).RootPath("./test/erp");
        development.Files(s_wms).RootPath("./test/wms");
        return development.Build();
    }

    [Theory]
    [InlineData("orders-claim-store")]
    [InlineData("orders-archive-store")]
    public void Generate_ShouldRenderEachStoreAsALocalFolderScopedToItsUsers(string store)
    {
        // Arrange
        var topology = Topology();
        var hostRoot = Directory.GetCurrentDirectory();

        // Act
        var artifacts = TopologyGenerator.Generate(topology, Development(topology), hostRoot);

        // Assert
        var yaml = Assert.Single(artifacts.Files, f => f.RelativePath == $"components/{store}.yaml").Content;
        Assert.Contains("type: \"bindings.localstorage\"", yaml, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(hostRoot, "test", store), yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-sync\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("stock-sync", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphJson_ShouldListTheStoresAndNameThemOnTheirUsers()
    {
        // Act
        using var json = JsonDocument.Parse(IntropyGenerate.GraphJson(Topology(), development: null));

        // Assert
        var stores = json.RootElement.GetProperty("stores").EnumerateArray()
            .Select(s => (s.GetProperty("name").GetString(), s.GetProperty("role").GetString(),
                string.Join(",", s.GetProperty("usedBy").EnumerateArray().Select(u => u.GetString()))))
            .ToArray();
        Assert.Equal(
            [("orders-archive-store", "archive", "order-sync"), ("orders-claim-store", "claim", "order-sync")],
            stores);

        var components = json.RootElement.GetProperty("components").EnumerateArray().ToArray();
        Assert.Equal("orders-claim-store", components[0].GetProperty("claimStore").GetString());
        Assert.Equal("orders-archive-store", components[0].GetProperty("archiveStore").GetString());
        Assert.False(components[1].TryGetProperty("claimStore", out _));
    }

    [Fact]
    public void GraphJson_WithoutStores_ShouldOmitThem()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddTransactionalIntegration("order-sync").From(s_erp).To(s_wms);

        // Act
        using var json = JsonDocument.Parse(IntropyGenerate.GraphJson(builder.Build(), development: null));

        // Assert
        Assert.False(json.RootElement.TryGetProperty("stores", out _));
    }
}
