namespace Intropy.Topology.Aspire.Test;

public sealed class ComponentOverlayTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("intropy-overlay-").FullName;

    private string AppHostDir => Path.Combine(_workspace, "system-host");

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private void WriteLocalComponent(string component, string fileName, string metadataName, string type)
    {
        var dir = Directory.CreateDirectory(
            Path.Combine(_workspace, component, "local", "dapr-components")).FullName;
        File.WriteAllText(Path.Combine(dir, fileName), ComponentYaml(metadataName, type));
    }

    private string WriteGeneratedComponent(string fileName, string metadataName, string type)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_workspace, "generated", "components")).FullName;
        File.WriteAllText(Path.Combine(dir, fileName), ComponentYaml(metadataName, type));
        return dir;
    }

    private static string ComponentYaml(string name, string type) =>
        $"""
        apiVersion: dapr.io/v1alpha1
        kind: Component
        metadata:
          name: "{name}"
        spec:
          type: "{type}"
          version: v1
        """;

    [Fact]
    public void Stage_ShouldPassLocalComponentsThrough_AndAddGeneratedOnes()
    {
        // Arrange
        WriteLocalComponent("order-extractor", "inbound.yaml", "inbound", "bindings.localstorage");
        var generated = WriteGeneratedComponent("pubsub-a.yaml", "pubsub-a", "pubsub.redis");

        // Act
        var staged = ComponentOverlay.Stage(AppHostDir, "order-extractor", generated);

        // Assert
        Assert.Equal(
            Path.Combine(AppHostDir, "obj", "dapr-components", "order-extractor"),
            staged);
        Assert.Equal(
            ["inbound.yaml", "pubsub-a.yaml"],
            Directory.GetFiles(staged).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Stage_WithSameMetadataName_ShouldReplaceLocalWithGenerated()
    {
        // Arrange — the tutorial's swap: local in-memory pubsub vs generated Redis-backed one,
        // keyed on metadata.name even though the filenames differ.
        WriteLocalComponent("order-extractor", "pubsub.yaml", "pubsub", "pubsub.in-memory");
        var generated = WriteGeneratedComponent("pubsub-generated.yaml", "pubsub", "pubsub.redis");

        // Act
        var staged = ComponentOverlay.Stage(AppHostDir, "order-extractor", generated);

        // Assert — one component named "pubsub" survives, and it is the generated one.
        var file = Assert.Single(Directory.GetFiles(staged));
        Assert.Contains("pubsub.redis", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void Stage_CalledAgain_ShouldDropFilesFromThePreviousRun()
    {
        // Arrange — a first staging leaves a file behind that the next topology no longer has.
        var generated = WriteGeneratedComponent("pubsub-a.yaml", "pubsub-a", "pubsub.redis");
        ComponentOverlay.Stage(AppHostDir, "order-extractor", generated);
        File.Delete(Path.Combine(generated, "pubsub-a.yaml"));
        File.WriteAllText(Path.Combine(generated, "pubsub-b.yaml"), ComponentYaml("pubsub-b", "pubsub.redis"));

        // Act
        var staged = ComponentOverlay.Stage(AppHostDir, "order-extractor", generated);

        // Assert
        Assert.Equal("pubsub-b.yaml", Path.GetFileName(Assert.Single(Directory.GetFiles(staged))));
    }

    [Fact]
    public void Stage_WithoutLocalOrGeneratedComponents_ShouldStageAnEmptyDirectory()
    {
        // Act — neither directory exists.
        var staged = ComponentOverlay.Stage(AppHostDir, "order-extractor", Path.Combine(_workspace, "missing"));

        // Assert
        Assert.True(Directory.Exists(staged));
        Assert.Empty(Directory.GetFiles(staged));
    }

    [Fact]
    public void MetadataName_ShouldReadTheTopLevelName_NotSpecMetadataEntries()
    {
        // Arrange — spec.metadata carries name/value pairs that must not be mistaken for identity.
        const string yaml = """
            apiVersion: dapr.io/v1alpha1
            kind: Component
            metadata:
              name: pubsub
            spec:
              type: pubsub.redis
              version: v1
              metadata:
                - name: hostname
                  value: localhost
            """;

        // Act & Assert
        Assert.Equal("pubsub", ComponentOverlay.MetadataName(yaml));
    }
}
