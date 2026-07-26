using System.Text.Json;

namespace Intropy.Topology.Generation.Test;

public class IntropyGenerateTests
{
    private static readonly System.Reflection.Assembly s_assembly = typeof(OrderSystem).Assembly;

    [Fact]
    public async Task RunAsync_Check_ShouldReturnZeroForValidSystem()
    {
        // Act
        var (code, _, _) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["check"]));

        // Assert
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task RunAsync_Graph_ShouldEmitTopologyV1Document()
    {
        // Act
        var (code, output, error) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["graph"]));
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;

        // Assert
        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        Assert.Equal("topology.intropy.io/v1", root.GetProperty("apiVersion").GetString());
        Assert.Equal("SystemTopology", root.GetProperty("kind").GetString());
        Assert.Equal("order-fulfillment", root.GetProperty("system").GetString());
        Assert.False(root.TryGetProperty("systemName", out _));
    }

    [Fact]
    public async Task RunAsync_Graph_ShouldEmitTheV1WiringAndLookupTables()
    {
        // Act
        var (_, output, _) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["graph"]));
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        var extractor = root.GetProperty("components").EnumerateArray()
            .Single(component => component.GetProperty("name").GetString() == "order-extractor");
        var rawTopic = root.GetProperty("topics").EnumerateArray()
            .Single(topic => topic.GetProperty("topic").GetString() == "order-raw");
        var webshop = root.GetProperty("connectors").EnumerateArray()
            .Single(connector => connector.GetProperty("name").GetString() == "webshop");

        // Assert
        Assert.Equal("extractor", extractor.GetProperty("kind").GetString());
        Assert.False(extractor.TryGetProperty("schedule", out _));
        Assert.False(extractor.TryGetProperty("subscribes", out _));
        Assert.Equal("webshop", extractor.GetProperty("connectors")[0].GetProperty("connector").GetString());
        Assert.Equal("in", extractor.GetProperty("connectors")[0].GetProperty("direction").GetString());
        Assert.Equal("pubsub-a", extractor.GetProperty("publishes")[0].GetProperty("pubsub").GetString());
        Assert.False(extractor.GetProperty("publishes")[0].TryGetProperty("pubSub", out _));
        Assert.Equal("pubsub-a", rawTopic.GetProperty("pubsub").GetString());
        Assert.False(rawTopic.TryGetProperty("pubSub", out _));
        Assert.Equal("Intropy.Topology.Generation.Test.RawOrder", rawTopic.GetProperty("contract").GetString());
        Assert.Equal(["order-extractor"], rawTopic.GetProperty("publishers").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["order-loader", "raw-audit"], rawTopic.GetProperty("subscribers").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("file", webshop.GetProperty("transport").GetProperty("type").GetString());
        Assert.True(webshop.GetProperty("transport").GetProperty("supportsInput").GetBoolean());
        Assert.True(webshop.GetProperty("transport").GetProperty("supportsOutput").GetBoolean());
        Assert.Equal(["in"], webshop.GetProperty("directions").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["order-extractor"], webshop.GetProperty("usedBy").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task RunAsync_Graph_WhenSystemDiscoveryFails_ShouldWriteOnlyToStandardError()
    {
        // Act
        var (code, output, error) = await Capture(() => IntropyGenerate.RunAsync(typeof(IntropyGenerate).Assembly, ["graph"]));

        // Assert
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("error: No ISystemDefinition", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Generate_ShouldWriteArtifactsToDirectory()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), "intropy-gen-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Act
            var (code, _, _) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["generate", dir]));

            // Assert
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "components", "pubsub-a.yaml")));
            Assert.True(File.Exists(Path.Combine(dir, "config", "order-extractor.intropy.json")));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_UnknownVerb_ShouldReturnNonZero()
    {
        // Act
        var (code, _, _) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["wat"]));

        // Assert
        Assert.Equal(2, code);
    }

    private static async Task<(int Code, string Output, string Error)> Capture(Func<Task<int>> run)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var code = await run();
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
