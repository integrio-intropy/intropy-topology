using Intropy.Topology;
using Intropy.Topology.Model;

namespace Intropy.Topology.Generation.Test;

public class TopologyGeneratorTests
{
    private static readonly SystemTopology s_topology = SystemDiscovery.Discover(typeof(OrderSystem).Assembly).Topology;

    [Fact]
    public void Generate_ShouldEmit_OnePubSubComponentPerDistinctPubSubName()
    {
        // Act
        var artifacts = TopologyGenerator.Generate(s_topology);

        // Assert
        Assert.Contains(artifacts.Files, f => f.RelativePath == "components/pubsub-a.yaml");
        Assert.Contains(artifacts.Files, f => f.RelativePath == "components/pubsub-b.yaml");
    }

    [Fact]
    public void Generate_ShouldBackPubSubWithRabbitMq()
    {
        // Act
        var yaml = Content("components/pubsub-a.yaml");

        // Assert
        Assert.Contains("type: \"pubsub.rabbitmq\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: \"hostname\"", yaml, StringComparison.Ordinal);
        Assert.Contains("value: \"localhost\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: \"port\"", yaml, StringComparison.Ordinal);
        Assert.Contains("value: \"5672\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: \"username\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: \"password\"", yaml, StringComparison.Ordinal);
        Assert.Contains("value: \"guest\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldEmit_OneBindingComponentPerConnector_TypedFromTransport()
    {
        // Act
        var webshop = Content("components/binding.webshop.yaml");
        var erp = Content("components/binding.erp.yaml");

        // Assert
        Assert.Contains("type: \"bindings.localstorage\"", webshop, StringComparison.Ordinal);
        Assert.Contains("type: \"bindings.localstorage\"", erp, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldScope_PubSubToItsPublishersAndSubscribers()
    {
        // Act — pubsub-a is published by order-extractor and subscribed by order-loader.
        var yaml = Content("components/pubsub-a.yaml");

        // Assert
        Assert.Contains("scopes:", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-extractor\"", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-loader\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("fulfillment-extractor", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("fulfillment-loader", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithFileTransport_ShouldAbsolutizeRootPath()
    {
        // Act — artifacts land in per-run temp dirs, so the folder is anchored at generation time.
        var yaml = Content("components/binding.webshop.yaml");

        // Assert
        Assert.Contains("- name: \"rootPath\"", yaml, StringComparison.Ordinal);
        Assert.Contains($"value: \"{Path.GetFullPath("./test/webshop")}\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldScope_BindingToItsUsers()
    {
        // Act — webshop is used only by order-extractor.
        var yaml = Content("components/binding.webshop.yaml");

        // Assert
        Assert.Contains("scopes:", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"order-extractor\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("order-loader", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldEmit_PerComponentRuntimeConfig()
    {
        // Act
        var json = Content("config/order-extractor.intropy.json");

        // Assert
        Assert.Contains("\"System\": \"order-fulfillment\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Component\": \"order-extractor\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Kind\": \"Extractor\"", json, StringComparison.Ordinal);
        Assert.Contains("\"binding.webshop\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Direction\": \"In\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ShouldBeDeterministic()
    {
        // Act
        var first = TopologyGenerator.Generate(s_topology);
        var second = TopologyGenerator.Generate(s_topology);

        // Assert
        Assert.Equal(
            first.Files.Select(f => (f.RelativePath, f.Content)),
            second.Files.Select(f => (f.RelativePath, f.Content)));
    }

    private static string Content(string relativePath)
    {
        var artifacts = TopologyGenerator.Generate(s_topology);
        return artifacts.Files.Single(f => f.RelativePath == relativePath).Content;
    }
}
