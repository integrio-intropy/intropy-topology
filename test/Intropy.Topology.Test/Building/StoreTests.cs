using System.Text.Json;
using Intropy.Topology.Model;

namespace Intropy.Topology.Test.Building;

public class StoreTests
{
    private static readonly PortRef s_erp = PortRef.Define("erp");
    private static readonly PortRef s_wms = PortRef.Define("wms");
    private static readonly MessageRef<string> s_placed = MessageRef<string>.Define("order-placed");

    [Fact]
    public void Build_WithClaimChecksAndArchives_ShouldMintTheSystemsStoresScopedToTheirUsers()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("order-extractor").From(s_erp).Publishes(s_placed).ClaimChecks();
        builder.AddLoader("order-loader").Subscribes(s_placed).To(s_wms).ClaimChecks().Archives();
        builder.AddTransactionalIntegration("order-sync").From(s_erp).To(s_wms);

        // Act
        var topology = builder.Build();

        // Assert — one store per role, named after the system, listing only who declared it.
        Assert.NotNull(topology.Stores);
        Assert.Collection(topology.Stores,
            archive =>
            {
                Assert.Equal("orders-archive-store", archive.Name);
                Assert.Equal(StoreRole.Archive, archive.Role);
                Assert.Equal(["order-loader"], archive.UsedBy);
            },
            claim =>
            {
                Assert.Equal("orders-claim-store", claim.Name);
                Assert.Equal(StoreRole.Claim, claim.Role);
                Assert.Equal(["order-extractor", "order-loader"], claim.UsedBy);
            });
        var loader = topology.Components.Single(c => c.Name == "order-loader");
        Assert.Equal("orders-claim-store", loader.ClaimStore);
        Assert.Equal("orders-archive-store", loader.ArchiveStore);
        var sync = topology.Components.Single(c => c.Name == "order-sync");
        Assert.Null(sync.ClaimStore);
        Assert.Null(sync.ArchiveStore);
    }

    [Fact]
    public void Build_WithoutStores_ShouldSerializeAsBefore()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddTransactionalIntegration("order-sync").From(s_erp).To(s_wms);

        // Act
        var json = JsonSerializer.Serialize(builder.Build());

        // Assert
        Assert.DoesNotContain("Store", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenASubscriberOfAClaimCheckedTopicDoesNotClaimCheck_ShouldWarn()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("order-extractor").From(s_erp).Publishes(s_placed).ClaimChecks();
        builder.AddLoader("order-loader").Subscribes(s_placed).To(s_wms);

        // Act
        builder.TryBuild(out _, out var diagnostics);

        // Assert
        var diagnostic = Assert.Single(diagnostics, d => d.Message.Contains("ClaimChecks()", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("order-loader", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenAPortHasAStoresName_ShouldFail()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddTransactionalIntegration("order-sync")
            .From(PortRef.Define("orders-claim-store")).To(s_wms).ClaimChecks();

        // Act
        var built = builder.TryBuild(out _, out var diagnostics);

        // Assert
        Assert.False(built);
        Assert.Contains(diagnostics, d => d.Severity is DiagnosticSeverity.Error
            && d.Message.Contains("claim store 'orders-claim-store'", StringComparison.Ordinal));
    }
}
