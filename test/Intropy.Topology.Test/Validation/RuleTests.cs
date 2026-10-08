using Intropy.Topology.Building;
using Intropy.Topology.Validation;
using Intropy.Topology.Validation.Rules;

namespace Intropy.Topology.Test.Validation;

public sealed record OrderShipped;

internal static class ValidationTestHelper
{
    /// <summary>A publisher/subscriber pair that on its own violates nothing.</summary>
    public static SystemBuilder WithValidComponent(this SystemBuilder s)
    {
        s.AddExtractor("valid-extractor").Publishes(TestMessages.Raw);
        s.AddLoader("valid-sink").Subscribes(TestMessages.Raw);
        return s;
    }

    /// <summary>Runs a single model rule against the materialized system, isolating it from the others.</summary>
    public static IReadOnlyList<TopologyDiagnostic> DiagnosticsFor<TRule>(this SystemBuilder s)
        where TRule : IModelRule, new() =>
        new TRule().Evaluate(TopologyMaterializer.Materialize(s)).ToArray();

    /// <summary>Runs a single declaration rule against the raw builder, isolating it from the others.</summary>
    public static IReadOnlyList<TopologyDiagnostic> DeclarationDiagnosticsFor<TRule>(this SystemBuilder s)
        where TRule : IDeclarationRule, new() =>
        new TRule().Evaluate(s).ToArray();
}

public class DuplicateComponentNameRuleTests
{
    [Fact]
    public void Validate_WithDuplicateComponentName_ShouldReportError()
    {
        // Arrange: two components sharing one name
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("dup").Publishes(TestMessages.Raw);
        s.AddLoader("dup").Subscribes(TestMessages.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<DuplicateComponentNameRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("dup", diagnostic.Target);
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }
}

public class EmptySystemRuleTests
{
    [Fact]
    public void Validate_WithNoComponents_ShouldReportError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");

        // Act & Assert
        Assert.Single(s.DiagnosticsFor<EmptySystemRule>());
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }
}

public class PublishDeclarationTests
{
    [Fact]
    public void Publishes_WithTheSameMessageTwice_ShouldThrowAtTheDeclaration()
    {
        // Arrange: the same message declared twice — a redundant edge, not fan-out
        var s = SystemBuilder.Create("test-system");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            s.AddExtractor("extractor")
                .Publishes(TestMessages.Raw)
                .Publishes(TestMessages.Raw));
        Assert.Contains("extractor", exception.Message);
        Assert.Contains("raw-events", exception.Message);
    }

    [Fact]
    public void Publishes_WithASecondMessageOnTheSameChannel_ShouldMaterializeBothMessagesOnTheTopic()
    {
        // Arrange: several published messages may share one channel, as handled messages do
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .Publishes(MessageRef<RawEvent>.Define("raw-created", pubSub: "test-pubsub", topic: "shared"))
            .Publishes(MessageRef<EnrichedEvent>.Define("raw-cancelled", pubSub: "test-pubsub", topic: "shared"));

        // Act
        var topology = s.Build();

        // Assert
        var topic = Assert.Single(topology.Topics);
        Assert.Equal(["raw-cancelled", "raw-created"], topic.Messages);
        Assert.Equal(2, topology.Components[0].Publishes.Count);
    }

    [Fact]
    public void Publishes_WithDistinctMessagesOnDistinctChannels_ShouldDeclare()
    {
        // Arrange: several messages per extractor are legal — each resolving to its own channel
        var otherMessage = MessageRef<EnrichedEvent>.Define("other-topic", pubSub: "test-pubsub");
        var thirdMessage = MessageRef<RawEvent>.Define("third-topic", pubSub: "test-pubsub");
        var s = SystemBuilder.Create("test-system");

        // Act & Assert (no throw)
        s.AddExtractor("extractor").Publishes(otherMessage).Publishes(thirdMessage);
    }
}

public class MissingRequiredPortRuleTests
{
    [Fact]
    public void Validate_WhenTransactionalIntegrationHasFromAndTo_ShouldReportNothing()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti").From(TestPorts.Pim).To(TestPorts.Erp);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<MissingRequiredPortRule>());
    }

    [Fact]
    public void Validate_WhenTransactionalIntegrationLacksTo_ShouldReportError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti").From(TestPorts.Pim);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<MissingRequiredPortRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("ti", diagnostic.Target);
        Assert.Contains("To call", diagnostic.Message);
    }

    [Fact]
    public void Validate_WhenTransactionalIntegrationLacksFrom_ShouldReportError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti").To(TestPorts.Erp);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<MissingRequiredPortRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("ti", diagnostic.Target);
        Assert.Contains("From call", diagnostic.Message);
    }

    [Fact]
    public void Validate_WhenTransactionalIntegrationHasNoPorts_ShouldReportBothDirections()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti");

        // Act
        var diagnostics = s.DiagnosticsFor<MissingRequiredPortRule>();

        // Assert
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.All(diagnostics, d => Assert.Equal("ti", d.Target));
    }

    [Fact]
    public void Validate_WithMultiplePortsPerDirection_ShouldReportNothing()
    {
        // Arrange: several sources and destinations are legal — the rule checks presence.
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti")
            .From(TestPorts.Pim)
            .From(PortRef.Define("plm"))
            .To(TestPorts.Erp)
            .To(PortRef.Define("wms"));

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<MissingRequiredPortRule>());
    }

    [Fact]
    public void Build_WhenTransactionalIntegrationLacksTo_ShouldThrow()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti").From(TestPorts.Pim);

        // Act & Assert
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }

    [Fact]
    public void Validate_WithOtherKinds_ShouldReportNothing()
    {
        // Arrange: extractor and loader port edges stay optional by design.
        var s = SystemBuilder.Create("test-system").WithValidComponent();

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<MissingRequiredPortRule>());
    }
}

public class TopicCarryingSeveralContractsTests
{
    [Fact]
    public void Build_WithOneTopicCarryingTwoMessagesWithTheirOwnContracts_ShouldSucceed()
    {
        // Arrange: a channel carries several messages, each with its own contract
        var s = SystemBuilder.Create("test-system");
        var raw = MessageRef<RawEvent>.Define("shared-raw", pubSub: "test-pubsub", topic: "shared-topic");
        var enriched = MessageRef<EnrichedEvent>.Define("shared-enriched", pubSub: "test-pubsub", topic: "shared-topic");
        s.AddExtractor("first").Publishes(raw);
        s.AddExtractor("second").Publishes(enriched);
        s.AddLoader("consumer").Subscribes(raw, configure: sub => sub.AlsoHandles(enriched));

        // Act
        var topology = s.Build();

        // Assert
        var topic = Assert.Single(topology.Topics);
        Assert.Equal(["shared-enriched", "shared-raw"], topic.Messages);
    }
}

public class UnhandledChannelMessagesRuleTests
{
    private static readonly MessageRef<OrderPlaced> s_placed =
        MessageRef<OrderPlaced>.Define("fluxia.orders.order-placed", topic: "orders");

    private static readonly MessageRef<OrderCancelled> s_cancelled =
        MessageRef<OrderCancelled>.Define("fluxia.orders.order-cancelled", topic: "orders");

    private static readonly MessageRef<OrderShipped> s_shipped =
        MessageRef<OrderShipped>.Define("fluxia.orders.order-shipped", topic: "orders");

    private static SystemBuilder System(Action<LoaderBuilder> firstLoader, Action<LoaderBuilder>? secondLoader = null)
    {
        // One extractor per published message; the channel carries three messages with
        // their own contracts.
        var builder = SystemBuilder.Create("orders");
        builder.AddExtractor("placed-extractor").Publishes(s_placed);
        builder.AddExtractor("cancelled-extractor").Publishes(s_cancelled);
        builder.AddExtractor("shipped-extractor").Publishes(s_shipped);
        firstLoader(builder.AddLoader("fulfillment"));
        secondLoader?.Invoke(builder.AddLoader("archival"));
        return builder;
    }

    [Fact]
    public void Validate_WithUnhandledChannelMates_ShouldWarnWithTheirNamesAndTheComponentTarget()
    {
        // Arrange
        var builder = System(l => l.Subscribes(s_placed));

        // Act
        var diagnostic = Assert.Single(builder.DiagnosticsFor<UnhandledChannelMessagesRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("fulfillment", diagnostic.Target);
        Assert.Contains("handles 1 of 3 messages on topic 'orders' (pubsub 'pubsub')", diagnostic.Message);
        Assert.Contains("'fluxia.orders.order-cancelled', 'fluxia.orders.order-shipped'", diagnostic.Message);
    }

    [Fact]
    public void Build_WithOnlyThisWarning_ShouldSucceedAndTryBuildShouldCarryIt()
    {
        // Arrange
        var builder = System(l => l.Subscribes(s_placed));

        // Act & Assert: a warning does not fail Build
        builder.Build();

        Assert.True(builder.TryBuild(out _, out var diagnostics));
        Assert.Contains(diagnostics, d => d.Severity is DiagnosticSeverity.Warning
            && d.Message.Contains("will dead-letter"));
        Assert.Contains(builder.Validate(), d => d.Message.Contains("will dead-letter"));
    }

    [Fact]
    public void Validate_WithIgnoreOthers_ShouldReportNothing()
    {
        // Arrange: acknowledging the channel's other messages is the deliberate choice
        var builder = System(l => l.Subscribes(s_placed, configure: sub => sub.IgnoreOthers()));

        // Act & Assert
        Assert.Empty(builder.DiagnosticsFor<UnhandledChannelMessagesRule>());
    }

    [Fact]
    public void Validate_WhenTheSubscriptionHandlesEveryMessage_ShouldReportNothing()
    {
        // Arrange
        var builder = System(l => l.Subscribes(s_placed, configure: sub => sub
            .AlsoHandles(s_cancelled)
            .AlsoHandles(s_shipped)));

        // Act & Assert
        Assert.Empty(builder.DiagnosticsFor<UnhandledChannelMessagesRule>());
    }

    [Fact]
    public void Validate_WithASingleMessageChannel_ShouldReportNothing()
    {
        // Arrange: the common case — the required message is the channel's only one
        var builder = SystemBuilder.Create("single");
        builder.AddExtractor("extractor").Publishes(TestMessages.Raw);
        builder.AddLoader("sink").Subscribes(TestMessages.Raw);

        // Act & Assert
        Assert.Empty(builder.DiagnosticsFor<UnhandledChannelMessagesRule>());
    }

    [Fact]
    public void Validate_WithTwoLoadersOnOneChannel_ShouldWarnEachForItsOwnUnhandledMessages()
    {
        // Arrange: Dapr delivers every message to each consuming component's own group,
        // so each loader's channel-mate warnings are independent
        var builder = System(
            l => l.Subscribes(s_placed),
            l => l.Subscribes(s_shipped));

        // Act
        var diagnostics = builder.DiagnosticsFor<UnhandledChannelMessagesRule>();

        // Assert
        Assert.Equal(2, diagnostics.Count);
        var fulfillment = diagnostics.Single(d => d.Target == "fulfillment");
        Assert.Contains("'fluxia.orders.order-cancelled', 'fluxia.orders.order-shipped'", fulfillment.Message);
        var archival = diagnostics.Single(d => d.Target == "archival");
        Assert.Contains("'fluxia.orders.order-cancelled', 'fluxia.orders.order-placed'", archival.Message);
    }
}

public class MessageChannelConflictRuleTests
{
    [Fact]
    public void Validate_WithSameMessageOnTwoChannels_ShouldReportError()
    {
        // Arrange: one message identity declared on two transport channels
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("first")
            .Publishes(MessageRef<RawEvent>.Define("shared", pubSub: "test-pubsub", topic: "first-topic"));
        s.AddLoader("second")
            .Subscribes(MessageRef<RawEvent>.Define("shared", pubSub: "test-pubsub", topic: "second-topic"));

        // Act
        var diagnostic = Assert.Single(s.DeclarationDiagnosticsFor<MessageChannelConflictRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("shared", diagnostic.Target);
        Assert.Contains("conflicting channels", diagnostic.Message);
    }

    [Fact]
    public void Validate_WithDistinctMessageNames_ShouldReportNothing()
    {
        // Arrange: different names may live on different channels freely
        var s = SystemBuilder.Create("test-system").WithValidComponent();

        // Act & Assert
        Assert.Empty(s.DeclarationDiagnosticsFor<MessageChannelConflictRule>());
    }
}

public class MessageContractConflictRuleTests
{
    [Fact]
    public void Validate_WithSameMessageUnderTwoContracts_ShouldReportError()
    {
        // Arrange: one message identity declared with two contract types
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("first")
            .Publishes(MessageRef<RawEvent>.Define("shared", pubSub: "test-pubsub"));
        s.AddLoader("second")
            .Subscribes(MessageRef<EnrichedEvent>.Define("shared", pubSub: "test-pubsub"));

        // Act
        var diagnostic = Assert.Single(s.DeclarationDiagnosticsFor<MessageContractConflictRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("shared", diagnostic.Target);
        Assert.Contains(typeof(RawEvent).FullName!, diagnostic.Message);
        Assert.Contains(typeof(EnrichedEvent).FullName!, diagnostic.Message);
    }
}

public class PubSubPortNameCollisionRuleTests
{
    [Fact]
    public void Validate_WithPubSubNameEqualToDerivedComponentName_ShouldReportError()
    {
        // Arrange: port "shared" collides with a pubsub named "shared" — both become
        // Dapr Component names in the same namespace.
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .From(PortRef.Define("shared"))
            .Publishes(MessageRef<RawEvent>.Define("some-topic", pubSub: "shared", topic: "some-topic"));

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<PubSubPortNameCollisionRule>());

        // Assert
        Assert.Equal("shared", diagnostic.Target);
        Assert.Contains("shared", diagnostic.Message);
    }
}

public class AllowedPatternsTests
{
    [Fact]
    public void Validate_WithMultipleDistinctPortsOnOneComponent_ShouldNotReportErrors()
    {
        // Arrange: a transactional integration touching two externals
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti")
            .From(TestPorts.Pim)
            .To(TestPorts.Erp);

        // Act & Assert
        Assert.Empty(s.Validate());
    }
}
