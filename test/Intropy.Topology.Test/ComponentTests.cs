using Intropy.Topology.Model;

namespace Intropy.Topology.Test;

public class ComponentTests
{
    [Fact]
    public void Component_AfterFullChain_ShouldExposeNameAndKind()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");

        // Act
        ExtractorComponent component = s.AddExtractor("pim-extractor")
            .From(TestPorts.Pim)
            .Publishes(TestTopics.Raw)
            .Component;

        // Assert
        Assert.Equal("pim-extractor", component.Name);
        Assert.Equal(ComponentKind.Extractor, component.Kind);
    }

    [Fact]
    public void Component_AtAnyChainPosition_ShouldBeSameInstance()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        var builder = s.AddLoader("loader").Subscribes(TestTopics.Raw);

        // Act
        var beforeTo = builder.Component;
        var afterTo = builder.To(TestPorts.Erp).Component;

        // Assert
        Assert.Same(beforeTo, afterTo);
    }

    [Fact]
    public void Component_PerBlockKind_ShouldHaveDistinctType()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");

        // Act & Assert
        Assert.IsType<ExtractorComponent>(s.AddExtractor("a").Component);
        Assert.IsType<LoaderComponent>(s.AddLoader("d").Component);
        Assert.IsType<TransactionalIntegrationComponent>(s.AddTransactionalIntegration("e").Component);
    }

    [Fact]
    public void Component_AsBaseType_ShouldSupportUniformCollections()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");

        // Act
        List<Component> components =
        [
            s.AddExtractor("a").Publishes(TestTopics.Raw).Component,
            s.AddLoader("b").Subscribes(TestTopics.Raw).To(TestPorts.Erp).Component,
        ];

        // Assert
        Assert.Equal(["a", "b"], components.Select(c => c.Name));
        Assert.Equal([ComponentKind.Extractor, ComponentKind.Loader], components.Select(c => c.Kind));
    }

    [Fact]
    public void Component_HeldHandle_ShouldMatchBuiltModelName()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        var handle = s.AddExtractor("pim-extractor")
            .Publishes(TestTopics.Raw)
            .Component;
        s.AddLoader("sink").Subscribes(TestTopics.Raw);

        // Act
        var topology = s.Build();

        // Assert
        var model = topology.Components[0];
        Assert.Equal(handle.Name, model.Name);
        Assert.Equal(handle.Kind, model.Kind);
    }
}
