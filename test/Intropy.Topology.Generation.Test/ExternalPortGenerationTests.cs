using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation.Test;

public class ExternalPortGenerationTests
{
    private static readonly PortRef s_campaigns = PortRef.DefineExternal("campaigns");
    private static readonly PortRef s_erp = PortRef.Define("erp");

    private static SystemTopology Topology()
    {
        var message = MessageRef<string>.Define("campaign-changed");
        var builder = SystemBuilder.Create("campaign-sync");
        builder.AddExtractor("campaign-extractor").From(s_campaigns).Publishes(message);
        builder.AddExtractor("erp-extractor").From(s_erp).Publishes(message);
        builder.AddLoader("campaign-loader").Subscribes(message);
        return builder.Build();
    }

    private static DevelopmentManifest Development(SystemTopology topology)
    {
        var development = new DevelopmentBuilder(topology, Directory.GetCurrentDirectory());
        development.Files(s_erp).RootPath("./test/erp");
        development.Settings(s_campaigns).Set("ConnectionString", "Server=localhost;Password=secret");
        return development.Build();
    }

    private static string Content(GeneratedArtifacts artifacts, string path) =>
        Assert.Single(artifacts.Files, f => f.RelativePath == path).Content;

    [Fact]
    public void Generate_WithExternalPort_ShouldRenderNoBinding()
    {
        // Act
        var topology = Topology();
        var artifacts = TopologyGenerator.Generate(topology, Development(topology), Directory.GetCurrentDirectory());

        // Assert
        Assert.DoesNotContain(artifacts.Files, f => f.RelativePath == "components/campaigns.yaml");
        Assert.Contains(artifacts.Files, f => f.RelativePath == "components/erp.yaml");
    }

    [Fact]
    public void Generate_WithExternalPort_ShouldRenderCursorStoreScopedToItsReader()
    {
        // Act
        var topology = Topology();
        var yaml = Content(TopologyGenerator.Generate(topology, Development(topology), Directory.GetCurrentDirectory()),
            "components/campaign-extractor-cursors.yaml");

        // Assert
        Assert.Contains("type: \"state.redis\"", yaml, StringComparison.Ordinal);
        Assert.Contains("value: \"localhost:6380\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"campaign-extractor\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("erp-extractor", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldNeverWritePortSettings()
    {
        // Act
        var topology = Topology();
        var artifacts = TopologyGenerator.Generate(topology, Development(topology), Directory.GetCurrentDirectory());

        // Assert — a setting such as a connection string stays out of every artifact.
        Assert.DoesNotContain(artifacts.Files, f => f.Content.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_ShouldWriteCursorStoreAndExternalKindIntoComponentConfig()
    {
        // Act
        var topology = Topology();
        var json = Content(TopologyGenerator.Generate(topology, Development(topology), Directory.GetCurrentDirectory()),
            "config/campaign-extractor.intropy.json");

        // Assert
        Assert.Contains("\"CursorStore\": \"campaign-extractor-cursors\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Kind\": \"External\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"DaprComponent\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphJson_ShouldMarkExternalPortAndListSettingKeysOnly()
    {
        // Act
        var topology = Topology();
        var json = IntropyGenerate.GraphJson(topology, Development(topology));

        // Assert
        Assert.Contains("\"kind\": \"external\"", json, StringComparison.Ordinal);
        Assert.Contains("\"cursorStore\": \"campaign-extractor-cursors\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ConnectionString\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithUnresolvedExternalPort_ShouldSucceed()
    {
        // Arrange — an external port needs no folder or mock.
        var topology = Topology();
        var development = new DevelopmentBuilder(topology, Directory.GetCurrentDirectory());
        development.Files(s_erp).RootPath("./test/erp");

        // Act
        var manifest = development.Build();

        // Assert
        Assert.Empty(manifest.Settings);
    }

    [Fact]
    public void Files_WithExternalPort_ShouldThrow()
    {
        // Arrange
        var development = new DevelopmentBuilder(Topology(), Directory.GetCurrentDirectory());

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => development.Files(s_campaigns));
        Assert.Contains("Settings(port)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mock_WithExternalPort_ShouldThrow()
    {
        // Arrange
        var development = new DevelopmentBuilder(Topology(), Directory.GetCurrentDirectory());

        // Act & Assert
        Assert.Throws<DevelopmentValidationException>(() => development.Mock(s_campaigns));
    }

    [Fact]
    public void Settings_WithBindingPort_ShouldThrow()
    {
        // Arrange
        var development = new DevelopmentBuilder(Topology(), Directory.GetCurrentDirectory());

        // Act & Assert
        var exception = Assert.Throws<DevelopmentValidationException>(() => development.Settings(s_erp));
        Assert.Contains("only an external port takes settings", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CursorStore")]
    [InlineData("Connection:String")]
    [InlineData("")]
    public void Set_WithReservedOrInvalidKey_ShouldThrow(string key)
    {
        // Arrange
        var settings = new DevelopmentBuilder(Topology(), Directory.GetCurrentDirectory()).Settings(s_campaigns);

        // Act & Assert
        Assert.Throws<DevelopmentValidationException>(() => settings.Set(key, "value"));
    }

    [Fact]
    public void Set_WithDuplicateKey_ShouldThrow()
    {
        // Arrange
        var settings = new DevelopmentBuilder(Topology(), Directory.GetCurrentDirectory()).Settings(s_campaigns);
        settings.Set("ConnectionString", "a");

        // Act & Assert
        Assert.Throws<DevelopmentValidationException>(() => settings.Set("connectionstring", "b"));
    }
}
