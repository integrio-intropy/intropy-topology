using Intropy.Topology;
using Intropy.Topology.Generation;
using Intropy.Topology.Model;

namespace Intropy.Topology.Aspire.Test;

public class ExternalPortVariablesTests
{
    private static readonly PortRef s_campaigns = PortRef.DefineExternal("campaigns");
    private static readonly PortRef s_erp = PortRef.Define("erp");

    private static (SystemTopology Topology, DevelopmentManifest Development) System()
    {
        var message = MessageRef<string>.Define("campaign-changed");
        var builder = SystemBuilder.Create("campaign-sync");
        builder.AddExtractor("campaign-extractor").From(s_campaigns).Publishes(message);
        builder.AddExtractor("erp-extractor").From(s_erp).Publishes(message);
        builder.AddLoader("campaign-loader").Subscribes(message).To(s_campaigns);
        var topology = builder.Build();
        var development = new DevelopmentBuilder(topology, Directory.GetCurrentDirectory());
        development.Files(s_erp).RootPath("./test/erp");
        development.Settings(s_campaigns).Set("ConnectionString", "Server=localhost").Set("BaseUrl", "http://localhost:8080");
        return (topology, development.Build());
    }

    [Fact]
    public void ExternalPortVariables_ShouldPassCursorStoreAndSettingsInThePortSection()
    {
        // Arrange
        var (topology, development) = System();
        var component = topology.Components.Single(c => c.Name == "campaign-extractor");

        // Act
        var variables = IntropyAspire.ExternalPortVariables(topology, component, development);

        // Assert
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Ports__campaigns__CursorStore"] = "campaign-extractor-cursors",
                ["Ports__campaigns__ConnectionString"] = "Server=localhost",
                ["Ports__campaigns__BaseUrl"] = "http://localhost:8080",
            },
            variables);
    }

    [Fact]
    public void ExternalPortVariables_ForComponentWithoutExternalPort_ShouldBeEmpty()
    {
        // Arrange
        var (topology, development) = System();
        var component = topology.Components.Single(c => c.Name == "erp-extractor");

        // Act & Assert
        Assert.Empty(IntropyAspire.ExternalPortVariables(topology, component, development));
    }

    [Fact]
    public void ExternalPortVariables_ForWriter_ShouldPassSettingsWithoutCursorStore()
    {
        // Arrange
        var (topology, development) = System();
        var component = topology.Components.Single(c => c.Name == "campaign-loader");

        // Act
        var variables = IntropyAspire.ExternalPortVariables(topology, component, development);

        // Assert
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Ports__campaigns__ConnectionString"] = "Server=localhost",
                ["Ports__campaigns__BaseUrl"] = "http://localhost:8080",
            },
            variables);
    }
}
