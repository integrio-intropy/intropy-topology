using Intropy.Topology;

namespace Intropy.Topology.Aspire.Test;

public class StoreVariablesTests
{
    private static readonly PortRef s_erp = PortRef.Define("erp");
    private static readonly PortRef s_wms = PortRef.Define("wms");

    [Fact]
    public void StoreVariables_ShouldPointEachSectionAtItsStoreAsALocalFolder()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddTransactionalIntegration("order-sync").From(s_erp).To(s_wms).ClaimChecks().Archives();
        var component = builder.Build().Components.Single();

        // Act
        var variables = IntropyAspire.StoreVariables(component);

        // Assert
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["ClaimStore__DaprBindingName"] = "orders-claim-store",
                ["ClaimStore__Kind"] = "Local",
                ["ArchiveStore__DaprBindingName"] = "orders-archive-store",
                ["ArchiveStore__Kind"] = "Local",
            },
            variables);
    }

    [Fact]
    public void StoreVariables_ForAComponentUsingNoStore_ShouldBeEmpty()
    {
        // Arrange
        var builder = SystemBuilder.Create("orders");
        builder.AddTransactionalIntegration("order-sync").From(s_erp).To(s_wms);

        // Act & Assert
        Assert.Empty(IntropyAspire.StoreVariables(builder.Build().Components.Single()));
    }
}
