using Intropy.Topology.Validation;
using Intropy.Topology.Validation.Rules;

namespace Intropy.Topology.Test.Validation;

public sealed record ProcessedRecord;

public class ProcessorRuleTests
{
    private static readonly MessageRef<ProcessedRecord> s_processed =
        MessageRef<ProcessedRecord>.Define("processed-records", pubSub: "test-pubsub");

    [Fact]
    public void MissingRequiredOutput_WithAProcessorThatPublishesNothing_ShouldError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddProcessor("order-totals").Subscribes(TestMessages.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<MissingRequiredOutputRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("order-totals", diagnostic.Target);
        Assert.Contains("Processor components must publish", diagnostic.Message);
    }

    [Fact]
    public void MissingRequiredSubscription_WithAProcessorThatSubscribesToNothing_ShouldError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddProcessor("order-totals").Publishes(s_processed);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<MissingRequiredSubscriptionRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Processor components must subscribe", diagnostic.Message);
    }

    [Fact]
    public void ProcessorRepublish_WithAPublishedMessageItHandles_ShouldError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddProcessor("echo").Subscribes(TestMessages.Raw).Publishes(TestMessages.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<ProcessorRepublishRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("echo", diagnostic.Target);
        Assert.Contains("'raw-events'", diagnostic.Message);
    }

    [Fact]
    public void ProcessorRepublish_WithANewMessage_ShouldPass()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddProcessor("order-totals").Subscribes(TestMessages.Raw).Publishes(s_processed);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<ProcessorRepublishRule>());
    }

    [Fact]
    public void StateStoreNameCollision_WithAPortNamedLikeTheState_ShouldError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddProcessor("orders").Subscribes(TestMessages.Raw).Publishes(s_processed);
        s.AddExtractor("lookup").From(PortRef.Define("orders-state")).Publishes(TestMessages.Enriched);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<StateStoreNameCollisionRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("orders-state", diagnostic.Target);
        Assert.Contains("port 'orders-state'", diagnostic.Message);
    }

    [Fact]
    public void StateStoreNameCollision_WithAPubSubNamedLikeTheOutboxState_ShouldError()
    {
        // Arrange
        var collides = MessageRef<ProcessedRecord>.Define("collides", pubSub: "orders-state-outbox");
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddProcessor("orders").Subscribes(TestMessages.Raw).Publishes(collides);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<StateStoreNameCollisionRule>());

        // Assert
        Assert.Equal("orders-state-outbox", diagnostic.Target);
        Assert.Contains("a pubsub", diagnostic.Message);
    }

    [Fact]
    public void StateStoreNameCollision_WithAProcessorStateNamedLikeALoadersWorkflowState_ShouldError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddLoader("orders").Subscribes(TestMessages.Raw).WithWorkflowState();
        s.AddProcessor("orders-workflow").Subscribes(TestMessages.Raw).Publishes(s_processed);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<StateStoreNameCollisionRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("orders-workflow-state", diagnostic.Target);
        Assert.Contains("Loader 'orders's workflow state component", diagnostic.Message);
    }

    [Fact]
    public void StateStoreNameCollision_WithAPortNamedLikeAWorkflowState_ShouldError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();
        s.AddLoader("orders").Subscribes(TestMessages.Raw).To(PortRef.Define("orders-workflow-state")).WithWorkflowState();

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<StateStoreNameCollisionRule>());

        // Assert
        Assert.Equal("orders-workflow-state", diagnostic.Target);
        Assert.Contains("port 'orders-workflow-state'", diagnostic.Message);
    }

    [Fact]
    public void Build_WithACompleteProcessor_ShouldValidate()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("source").Publishes(TestMessages.Raw);
        s.AddProcessor("order-totals").Subscribes(TestMessages.Raw).Publishes(s_processed);
        s.AddLoader("sink").Subscribes(s_processed);

        // Act
        var diagnostics = s.Validate();

        // Assert
        Assert.Empty(diagnostics);
    }
}
