using Intropy.Topology.Building;
using Intropy.Topology.Validation;
using Intropy.Topology.Validation.Rules;

namespace Intropy.Topology.Test.Validation;

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

public class DuplicatePublishRuleTests
{
    [Fact]
    public void Validate_WithDuplicateMessagePublish_ShouldReportError()
    {
        // Arrange: the same message declared twice — a redundant edge, not fan-out
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .Publishes(TestMessages.Raw)
            .Publishes(TestMessages.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<DuplicatePublishRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("extractor", diagnostic.Target);
        Assert.Equal(
            "The message 'raw-events' is published more than once by the same component.",
            diagnostic.Message);
    }

    [Fact]
    public void Validate_WithSinglePublish_ShouldReportNothing()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<DuplicatePublishRule>());
    }

    [Fact]
    public void Validate_WithTwoMessagesOnOneChannel_ShouldReportNothing()
    {
        // Arrange: a channel carries several messages; one extractor may publish them all
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .Publishes(MessageRef<RawEvent>.Define("raw-created", "test-pubsub", "shared"))
            .Publishes(MessageRef<EnrichedEvent>.Define("raw-cancelled", "test-pubsub", "shared"));

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<DuplicatePublishRule>());
    }

    [Fact]
    public void Validate_WithDistinctMessages_ShouldReportNothing()
    {
        // Arrange: several messages per extractor are legal — each on its own channel
        var otherMessage = MessageRef<EnrichedEvent>.Define("other-topic", "test-pubsub");
        var thirdMessage = MessageRef<RawEvent>.Define("third-topic", "test-pubsub");
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .Publishes(otherMessage)
            .Publishes(thirdMessage);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<DuplicatePublishRule>());
    }
}

public class DuplicateSubscriptionRuleTests
{
    [Fact]
    public void Validate_WithDuplicateSubscription_ShouldReportError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader")
            .Subscribes(TestMessages.Raw)
            .Subscribes(TestMessages.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<DuplicateSubscriptionRule>());

        // Assert
        Assert.Equal("loader", diagnostic.Target);
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
        var raw = MessageRef<RawEvent>.Define("shared-raw", "test-pubsub", "shared-topic");
        var enriched = MessageRef<EnrichedEvent>.Define("shared-enriched", "test-pubsub", "shared-topic");
        s.AddExtractor("first").Publishes(raw);
        s.AddExtractor("second").Publishes(enriched);
        s.AddLoader("consumer").Subscribes(sub => sub.Handles(raw).Handles(enriched));

        // Act
        var topology = s.Build();

        // Assert
        var topic = Assert.Single(topology.Topics);
        Assert.Equal(["shared-enriched", "shared-raw"], topic.Messages);
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
            .Publishes(MessageRef<RawEvent>.Define("shared", "test-pubsub", "first-topic"));
        s.AddLoader("second")
            .Subscribes(MessageRef<RawEvent>.Define("shared", "test-pubsub", "second-topic"));

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
            .Publishes(MessageRef<RawEvent>.Define("shared", "test-pubsub"));
        s.AddLoader("second")
            .Subscribes(MessageRef<EnrichedEvent>.Define("shared", "test-pubsub"));

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
            .Publishes(MessageRef<RawEvent>.Define("some-topic", "shared", "some-topic"));

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
