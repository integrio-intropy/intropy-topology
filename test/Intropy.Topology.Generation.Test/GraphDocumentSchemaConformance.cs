using System.Reflection;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Intropy.Topology.Generation.Test;

// The graph record is the contract every consumer (the intropy CLI, the
// frontend) decodes. These tests pin the emitter to the schema in
// schemas/topology-graph.schema.json by validating the graph verb's actual
// stdout — the wire, not the private record that produced it — so a change
// that breaks the contract fails this repo's own build, not a downstream
// consumer.
// Console-capturing tests must not run in parallel with each other: Capture
// redirects the shared Console.Out, and two concurrent captures corrupt one
// another's output. Sharing one xUnit collection serializes them.
[Collection("ConsoleCapture")]
public class GraphDocumentSchemaConformance
{
    private static readonly Assembly s_assembly = typeof(OrderSystem).Assembly;

    private static JsonSchema LoadGraphSchema()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "schemas", "topology-graph.schema.json"));
        return JsonSchema.FromText(File.ReadAllText(path));
    }

    private static JsonNode GraphJson(Assembly assembly, params string[] args)
    {
        var (code, output, error) = IntropyGenerateTests.Capture(() => IntropyGenerate.Run(assembly, args));
        Assert.Equal(0, code);
        // stderr may carry discovery warnings (the WipSystem fixture declares a
        // deliberately one-sided topic); stdout must still be pure JSON.
        Assert.DoesNotContain("error: ", error, StringComparison.Ordinal);
        return JsonNode.Parse(output)!;
    }

    private static void AssertValid(JsonNode? instance, string because)
    {
        var results = LoadGraphSchema().Evaluate(instance, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });
        Assert.True(results.IsValid,
            $"{because}: {System.Text.Json.JsonSerializer.Serialize(results.Details.Where(d => d.HasErrors).Select(d => new { d.InstanceLocation, d.Errors }))}");
    }

    [Fact]
    public void Graph_FullSystem_ConformsToSchema()
    {
        // OrderSystem exercises every emitted section: all three block kinds
        // (the transactional integration mints an internalQueue), topics with
        // contracts, ports with uses, and the wiring lookup tables.
        AssertValid(GraphJson(s_assembly, "graph"), "full-system graph record must validate");
    }

    [Fact]
    public void Graph_WithDevelopmentSection_ConformsToSchema()
    {
        // --development adds the local-run section; it must validate too.
        AssertValid(GraphJson(s_assembly, "graph", "--development"), "graph record with development must validate");
    }

    [Fact]
    public void Graph_TopicFreeSystem_ConformsToSchema()
    {
        // The WipSystem fixture declares a transactional integration with no
        // topics: topics, services and the contracts they would carry are
        // legitimately absent — the schema must not require them.
        AssertValid(GraphJson(WipSystemFixture.Assembly, "graph"), "topic-free graph record must validate");
    }

    [Fact]
    public void Graph_WithExtraField_StillValidates()
    {
        // Additive-only guarantee: a forward-compatible emitter that adds a
        // field an older consumer does not know still validates.
        var json = GraphJson(s_assembly, "graph").AsObject();
        json["someFutureField"] = "added by a newer emitter";

        AssertValid(json, "unknown additive fields must validate");
    }

    [Fact]
    public void Graph_MissingSystem_FailsValidation()
    {
        var json = GraphJson(s_assembly, "graph").AsObject();
        json.Remove("system");

        Assert.False(LoadGraphSchema().Evaluate(json).IsValid, "a record without its system name must fail");
    }

    [Fact]
    public void Graph_WrongApiVersion_FailsValidation()
    {
        var json = GraphJson(s_assembly, "graph").AsObject();
        json["apiVersion"] = "topology.intropy.io/v0";

        Assert.False(LoadGraphSchema().Evaluate(json).IsValid, "a record at the wrong apiVersion must fail");
    }
}
