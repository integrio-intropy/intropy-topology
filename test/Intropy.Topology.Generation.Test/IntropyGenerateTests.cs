using System.Text.Json;

namespace Intropy.Topology.Generation.Test;

public class IntropyGenerateTests
{
    private static readonly System.Reflection.Assembly s_assembly = typeof(OrderSystem).Assembly;

    [Fact]
    public void Run_Check_ShouldReturnZeroForValidSystem()
    {
        // Act
        var (code, _, _) = Capture(() => IntropyGenerate.Run(s_assembly, ["check"]));

        // Assert
        Assert.Equal(0, code);
    }

    [Fact]
    public void Run_Graph_ShouldEmitTopologyV1Document()
    {
        // Act
        var (code, output, error) = Capture(() => IntropyGenerate.Run(s_assembly, ["graph"]));
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
    public void Run_Graph_ShouldEmitTheV1WiringAndLookupTables()
    {
        // Act
        var (_, output, _) = Capture(() => IntropyGenerate.Run(s_assembly, ["graph"]));
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        var extractor = root.GetProperty("components").EnumerateArray()
            .Single(component => component.GetProperty("name").GetString() == "order-extractor");
        var rawTopic = root.GetProperty("topics").EnumerateArray()
            .Single(topic => topic.GetProperty("topic").GetString() == "order-raw");
        var webshop = root.GetProperty("ports").EnumerateArray()
            .Single(port => port.GetProperty("name").GetString() == "webshop");

        // Assert
        Assert.Equal("extractor", extractor.GetProperty("kind").GetString());
        Assert.False(extractor.TryGetProperty("schedule", out _));
        Assert.False(extractor.TryGetProperty("subscribes", out _));
        Assert.Equal("erp", extractor.GetProperty("ports")[0].GetProperty("port").GetString());
        Assert.Equal("in", extractor.GetProperty("ports")[0].GetProperty("direction").GetString());
        Assert.Equal("pubsub-a", extractor.GetProperty("publishes")[0].GetProperty("pubsub").GetString());
        Assert.False(extractor.GetProperty("publishes")[0].TryGetProperty("pubSub", out _));
        Assert.Equal("pubsub-a", rawTopic.GetProperty("pubsub").GetString());
        Assert.False(rawTopic.TryGetProperty("pubSub", out _));
        Assert.Equal("Intropy.Topology.Generation.Test.RawOrder", rawTopic.GetProperty("contract").GetString());
        Assert.Equal(["order-extractor"], rawTopic.GetProperty("publishers").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["order-loader", "raw-audit"], rawTopic.GetProperty("subscribers").EnumerateArray().Select(value => value.GetString()));
        Assert.False(webshop.TryGetProperty("transport", out _));
        Assert.Equal(["out"], webshop.GetProperty("directions").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["order-loader", "price-sync"], webshop.GetProperty("usedBy").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void Run_Graph_ShouldEmit_InternalQueueForTransactionalIntegrationsOnly()
    {
        // Act
        var (_, output, _) = Capture(() => IntropyGenerate.Run(s_assembly, ["graph"]));
        using var json = JsonDocument.Parse(output);
        var components = json.RootElement.GetProperty("components").EnumerateArray().ToArray();
        var ti = components.Single(component => component.GetProperty("name").GetString() == "price-sync");

        // Assert
        Assert.Equal("internal-price-sync", ti.GetProperty("internalQueue").GetProperty("pubsub").GetString());
        Assert.Equal("hop", ti.GetProperty("internalQueue").GetProperty("topic").GetString());
        foreach (var component in components.Where(c => c.GetProperty("name").GetString() != "price-sync"))
        {
            Assert.False(component.TryGetProperty("internalQueue", out _));
        }
    }

    [Fact]
    public void Run_Graph_WithoutDevelopmentFlag_ShouldOmitDevelopmentSection()
    {
        // Act
        var (code, output, _) = Capture(() => IntropyGenerate.Run(s_assembly, ["graph"]));
        using var json = JsonDocument.Parse(output);

        // Assert
        Assert.Equal(0, code);
        Assert.False(json.RootElement.TryGetProperty("development", out _));
    }

    [Fact]
    public void Run_Graph_WithDevelopmentFlag_ShouldEmitDevelopmentSection()
    {
        // Act
        var (code, output, _) = Capture(() => IntropyGenerate.Run(s_assembly, ["graph", "--development"]));
        using var json = JsonDocument.Parse(output);
        var development = json.RootElement.GetProperty("development");

        // Assert: no mocks declared in this assembly; both ports resolve to ./test folders
        Assert.Equal(0, code);
        Assert.False(development.TryGetProperty("mocks", out _));
        var files = development.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(2, files.Length);
        var webshop = files.Single(file => file.GetProperty("port").GetString() == "webshop");
        Assert.Equal(Path.Combine("test", "webshop"), webshop.GetProperty("rootPath").GetString());
        var erp = files.Single(file => file.GetProperty("port").GetString() == "erp");
        Assert.Equal(Path.Combine("test", "erp"), erp.GetProperty("rootPath").GetString());
    }

    [Fact]
    public void Run_Graph_WhenSystemDiscoveryFails_ShouldWriteOnlyToStandardError()
    {
        // Act
        var (code, output, error) = Capture(() => IntropyGenerate.Run(typeof(IntropyGenerate).Assembly, ["graph"]));

        // Assert
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("error: No ISystemDefinition", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Generate_ShouldWriteArtifactsToDirectory()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), "intropy-gen-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Act
            var (code, _, _) = Capture(() => IntropyGenerate.Run(s_assembly, ["generate", dir]));

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
    public void Run_Graph_WithOneSidedTopic_ShouldEmitGraphAndWarn()
    {
        // Act
        var (code, output, error) = Capture(() => IntropyGenerate.Run(WipSystemFixture.Assembly, ["graph"]));
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;

        // Assert — stdout stays pure JSON; the one-sided topic warns on stderr
        Assert.Equal(0, code);
        Assert.Equal("wip-system", root.GetProperty("system").GetString());
        Assert.Contains("warning: ", error, StringComparison.Ordinal);
        Assert.Contains("no component subscribes", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Graph_WipSystemWithDevelopmentFlag_ShouldEmitDevelopmentSection()
    {
        // Act
        var (code, output, _) = Capture(() => IntropyGenerate.Run(WipSystemFixture.Assembly, ["graph", "--development"]));
        using var json = JsonDocument.Parse(output);
        var files = json.RootElement.GetProperty("development").GetProperty("files").EnumerateArray().ToArray();

        // Assert
        Assert.Equal(0, code);
        var placeholder = Assert.Single(files);
        Assert.Equal("placeholder", placeholder.GetProperty("port").GetString());
    }

    [Fact]
    public void Run_UnknownVerb_ShouldReturnNonZero()
    {
        // Act
        var (code, _, _) = Capture(() => IntropyGenerate.Run(s_assembly, ["wat"]));

        // Assert
        Assert.Equal(2, code);
    }

    [Fact]
    public void Run_GenerateWithExtraPositionalArguments_ShouldReturnTwo()
    {
        // Act
        var (code, _, error) = Capture(() => IntropyGenerate.Run(s_assembly, ["generate", "./out", "extra-arg"]));

        // Assert
        Assert.Equal(2, code);
        Assert.Contains("unrecognized arguments", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithInvariantViolation_ShouldThrowRatherThanExit()
    {
        // Arrange: a manifest mock no topology service matches — a programming error,
        // not user input, so it must crash rather than surface as exit code 1.
        var topic = TopicRef<string>.Define("orders", "created");
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("extractor").Publishes(topic);
        builder.AddLoader("loader").Subscribes(topic);
        var manifest = new DevelopmentManifest(
            [new OpenApiMock("ghost-service", "/tmp/ghost.yaml", "Ghost", "1")],
            []);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            TopologyGenerator.Generate(builder.Build(), manifest, Directory.GetCurrentDirectory()));
    }

    private static (int Code, string Output, string Error) Capture(Func<int> run)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var code = run();
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
