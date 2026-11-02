using System.Text.Json;

namespace Intropy.Topology.Generation.Test;

/// <summary>
/// A loader that runs Dapr workflows gets one state component of its own, marked as its sidecar's
/// actor state store; locally on the pubsub's Redis. A loader without workflows gets nothing new.
/// </summary>
public class WorkflowStateGenerationTests
{
    private static readonly MessageRef<LineAdded> s_lines =
        MessageRef<LineAdded>.Define("order-line-added", topic: "order-lines");

    private static readonly DevelopmentManifest s_noDevelopment = new([], [], []);

    private static IReadOnlyList<GeneratedFile> Generate(bool workflows)
    {
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("line-extractor").Publishes(s_lines);
        var loader = builder.AddLoader("order-approvals").Subscribes(s_lines);
        if (workflows)
            loader.WithWorkflowState();
        return TopologyGenerator.Generate(builder.Build(), s_noDevelopment, Directory.GetCurrentDirectory()).Files;
    }

    [Fact]
    public void Generate_ForALoaderWithWorkflowState_ShouldEmitAnActorStateStoreScopedToIt()
    {
        // Act
        var yaml = Generate(workflows: true).Single(f => f.RelativePath == "components/order-approvals-workflow-state.yaml").Content;

        // Assert
        Assert.Contains("name: \"order-approvals-workflow-state\"", yaml, StringComparison.Ordinal);
        Assert.Contains("type: \"state.redis\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: \"actorStateStore\"\n    value: \"true\"", yaml, StringComparison.Ordinal);
        Assert.Contains("scopes:\n- \"order-approvals\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ForALoaderWithWorkflowState_ShouldNameTheStoreInItsRuntimeConfig()
    {
        // Act
        var json = Generate(workflows: true).Single(f => f.RelativePath == "config/order-approvals.intropy.json").Content;

        // Assert
        var intropy = JsonDocument.Parse(json).RootElement.GetProperty("Intropy");
        Assert.Equal("order-approvals-workflow-state", intropy.GetProperty("WorkflowStateStoreName").GetString());
    }

    [Fact]
    public void Generate_ForALoaderWithoutWorkflowState_ShouldEmitNoActorStateStore()
    {
        // Act
        var files = Generate(workflows: false);

        // Assert
        Assert.DoesNotContain(files, f => f.Content.Contains("actorStateStore", StringComparison.Ordinal));
    }
}
