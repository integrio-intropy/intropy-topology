namespace Intropy.Topology.Generation.Test;

public class IntropyGenerateTests
{
    private static readonly System.Reflection.Assembly s_assembly = typeof(OrderSystem).Assembly;

    [Fact]
    public async Task RunAsync_Check_ShouldReturnZeroForValidSystem()
    {
        // Act
        var (code, _) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["check"]));

        // Assert
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task RunAsync_Graph_ShouldEmitTopologyJson()
    {
        // Act
        var (code, output) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["graph"]));

        // Assert
        Assert.Equal(0, code);
        Assert.Contains("\"systemName\": \"order-fulfillment\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Graph_ShouldEmitDisplayShape_NotTheWireModel()
    {
        // Act — graph is presentation: camelCase, kebab-case kinds, short contract names,
        // empty collections omitted. The wire format stays plain model serialization.
        var (_, output) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["graph"]));

        // Assert
        Assert.Contains("\"kind\": \"extractor\"", output, StringComparison.Ordinal);
        Assert.Contains("\"contract\": \"RawOrder\"", output, StringComparison.Ordinal);
        Assert.Contains("\"pubsub\": \"pubsub-a\"", output, StringComparison.Ordinal);
        Assert.Contains("\"direction\": \"in\"", output, StringComparison.Ordinal);
        // Connector entries carry the resolved transport's Dapr type.
        Assert.Contains("\"transport\": \"bindings.localstorage\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Kind\":", output, StringComparison.Ordinal);
        Assert.DoesNotContain("ContractTypeName", output, StringComparison.Ordinal);
        // The extractor subscribes to nothing: the empty collection is omitted, not printed.
        Assert.DoesNotContain("\"subscribes\": []", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Graph_ShouldShowScheduleOnlyForScheduledComponents()
    {
        // Act — order-extractor declares a schedule; the other components do not.
        var (_, output) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["graph"]));

        // Assert
        Assert.Contains("\"schedule\": \"*/5 * * * *\"", output, StringComparison.Ordinal);
        Assert.Equal(1, output.Split("\"schedule\"").Length - 1);
    }

    [Fact]
    public async Task RunAsync_Generate_ShouldWriteArtifactsToDirectory()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), "intropy-gen-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Act
            var (code, _) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["generate", dir]));

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
        var (code, _) = await Capture(() => IntropyGenerate.RunAsync(s_assembly, ["wat"]));

        // Assert
        Assert.Equal(2, code);
    }

    private static async Task<(int Code, string Output)> Capture(Func<Task<int>> run)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var code = await run();
            return (code, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
