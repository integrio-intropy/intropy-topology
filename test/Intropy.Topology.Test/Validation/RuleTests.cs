using Intropy.Topology.Building;
using Intropy.Topology.Validation;
using Intropy.Topology.Validation.Rules;

namespace Intropy.Topology.Test.Validation;

internal static class ValidationTestHelper
{
    /// <summary>A publisher/subscriber pair that on its own violates nothing.</summary>
    public static SystemBuilder WithValidComponent(this SystemBuilder s)
    {
        s.AddExtractor("valid-extractor").Publishes(TestTopics.Raw);
        s.AddLoader("valid-sink").Subscribes(TestTopics.Raw);
        return s;
    }

    /// <summary>Runs a single rule against the materialized system, isolating it from the others.</summary>
    public static IReadOnlyList<TopologyDiagnostic> DiagnosticsFor<TRule>(this SystemBuilder s)
        where TRule : ITopologyRule, new() =>
        new TRule().Evaluate(new ValidationContext(s, TopologyMaterializer.Materialize(s))).ToArray();
}

public class DuplicateComponentNameRuleTests
{
    [Fact]
    public void Validate_WithDuplicateComponentName_ShouldReportError()
    {
        // Arrange: two components sharing one name
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("dup").Publishes(TestTopics.Raw);
        s.AddLoader("dup").Subscribes(TestTopics.Raw);

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

public class MultiplePublishesRuleTests
{
    [Fact]
    public void Validate_WithTwoPublishes_ShouldReportError()
    {
        // Arrange: a component publishes exactly one topic
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .Publishes(TestTopics.Raw)
            .Publishes(TestTopics.Enriched);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<DuplicatePortRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("extractor", diagnostic.Target);
        Assert.Equal(
            "The component declares 2 Publishes calls; a component publishes exactly one topic.",
            diagnostic.Message);
    }

    [Fact]
    public void Validate_WithSinglePublish_ShouldReportNothing()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<DuplicatePortRule>());
    }
}

public class ScheduleExpressionRuleTests
{
    [Theory]
    [InlineData("not a cron")]
    [InlineData("* * * * * *")]
    [InlineData("@fortnightly")]
    public void Validate_WithInvalidCron_ShouldReportError(string cron)
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("scheduled").Publishes(TestTopics.Raw).WithSchedule(cron);
        s.AddLoader("valid-sink").Subscribes(TestTopics.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<ScheduleExpressionRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("scheduled", diagnostic.Target);
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }

    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("0 9 * * 1-5")]
    [InlineData("@daily")]
    [InlineData("@MIDNIGHT")]
    public void Validate_WithValidCron_ShouldReportNothing(string cron)
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("scheduled").Publishes(TestTopics.Raw).WithSchedule(cron);
        s.AddLoader("valid-sink").Subscribes(TestTopics.Raw);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<ScheduleExpressionRule>());
    }

    [Fact]
    public void Validate_WithoutSchedule_ShouldReportNothing()
    {
        // Arrange: the schedule is optional — an unscheduled extractor runs once at host start
        var s = SystemBuilder.Create("test-system").WithValidComponent();

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<ScheduleExpressionRule>());
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
            .Subscribes(TestTopics.Raw)
            .Subscribes(TestTopics.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<DuplicateSubscriptionRule>());

        // Assert
        Assert.Equal("loader", diagnostic.Target);
    }
}

public class TopicContractConflictRuleTests
{
    [Fact]
    public void Validate_WithSameTopicUnderTwoContracts_ShouldReportError()
    {
        // Arrange: identical (pubsub, topic) declared with two contract types
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("first")
            .Publishes(TopicRef<RawEvent>.Define("test-pubsub", "shared-topic"));
        s.AddExtractor("second")
            .Publishes(TopicRef<EnrichedEvent>.Define("test-pubsub", "shared-topic"));

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<TopicContractConflictRule>());

        // Assert
        Assert.Contains(typeof(RawEvent).FullName!, diagnostic.Message);
        Assert.Contains(typeof(EnrichedEvent).FullName!, diagnostic.Message);
    }
}

public class ConnectorConflictRuleTests
{
    [Fact]
    public void Validate_WithSameConnectorNameForTwoTransports_ShouldReportError()
    {
        // Arrange: one connector name, two different transports (different file roots)
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("first")
            .From(ConnectorRef.Define("shared", Transport.File("./test/one")))
            .Publishes(TestTopics.Raw);
        s.AddTransactionalIntegration("second")
            .From(ConnectorRef.Define("shared", Transport.File("./test/other")));

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<ConnectorConflictRule>());

        // Assert
        Assert.Equal("shared", diagnostic.Target);
        Assert.Contains("conflicting transports", diagnostic.Message);
        Assert.Contains("bindings.localstorage", diagnostic.Message);
    }

    [Fact]
    public void Validate_WithSameConnectorDeclaredTwiceIdentically_ShouldReportNothing()
    {
        // Arrange: two usages of the same connector with the same transport
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("first")
            .From(ConnectorRef.Define("shared", Transport.File("./test/shared")))
            .Publishes(TestTopics.Raw);
        s.AddTransactionalIntegration("second")
            .From(ConnectorRef.Define("shared", Transport.File("./test/shared")));

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<ConnectorConflictRule>());
    }

    [Fact]
    public void Validate_WithSameLocalTransportButDifferentDeployedTransport_ShouldReportError()
    {
        // Arrange: a connector declaration includes both its local and deployed transport profiles
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("first")
            .To(ConnectorRef.Define("shared", Transport.File("./test/shared")).WithDeployed(Transport.Sftp()));
        s.AddTransactionalIntegration("second")
            .To(ConnectorRef.Define("shared", Transport.File("./test/shared")));

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<ConnectorConflictRule>());

        // Assert
        Assert.Equal("shared", diagnostic.Target);
        Assert.Contains("bindings.sftp", diagnostic.Message);
    }
}

public class DeployedTransportCapabilityRuleTests
{
    [Fact]
    public void Validate_WithInputOnlyUnsupportedSftpDeployedTransport_ShouldReportError()
    {
        // Arrange
        var connector = ConnectorRef.Define("shared", Transport.File("./test/shared"))
            .WithDeployed(Transport.Sftp());
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").From(connector).Publishes(TestTopics.Raw);
        s.AddLoader("loader").Subscribes(TestTopics.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<DeployedTransportCapabilityRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("extractor", diagnostic.Target);
        Assert.Contains("does not support input required by From", diagnostic.Message);
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }

    [Fact]
    public void Validate_WithSftpDeployedTransportForOutput_ShouldReportNothing()
    {
        // Arrange
        var connector = ConnectorRef.Define("shared", Transport.File("./test/shared"))
            .WithDeployed(Transport.Sftp());
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti").To(connector);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<DeployedTransportCapabilityRule>());
    }

    [Fact]
    public void Validate_WithoutDeployedTransport_ShouldReportNothing()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti").From(TestConnectors.Pim).To(TestConnectors.Pim);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<DeployedTransportCapabilityRule>());
    }
}

public class MissingRequiredOutputRuleTests
{
    [Fact]
    public void Validate_WithExtractorWithoutPublishes_ShouldReportError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").From(TestConnectors.Pim);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<MissingRequiredOutputRule>());

        // Assert
        Assert.Equal("extractor", diagnostic.Target);
        Assert.Contains("Publishes", diagnostic.Message);
    }

    [Fact]
    public void Validate_WithLoaderWithoutTo_ShouldReportNothing()
    {
        // Arrange: a loader's destination may stay a private local component
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").Subscribes(TestTopics.Raw);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<MissingRequiredOutputRule>());
    }

    [Fact]
    public void Validate_WithTransactionalIntegrationWithoutConnectors_ShouldReportNothing()
    {
        // Arrange: TI has no required output; an empty one is only a no-edges warning
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti");

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<MissingRequiredOutputRule>());
    }
}

public class MissingRequiredSubscriptionRuleTests
{
    [Fact]
    public void Validate_WithLoaderSubscribingToTwoTopics_ShouldReportError()
    {
        // Arrange: a loader consumes exactly one topic
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader")
            .Subscribes(TestTopics.Raw)
            .Subscribes(TestTopics.Enriched)
            .To(TestConnectors.Erp);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<MissingRequiredSubscriptionRule>());

        // Assert
        Assert.Contains("exactly one topic", diagnostic.Message);
    }

    [Fact]
    public void Validate_WithLoaderWithoutSubscription_ShouldReportError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").To(TestConnectors.Erp);

        // Act & Assert
        Assert.Single(s.DiagnosticsFor<MissingRequiredSubscriptionRule>());
    }
}

public class UnconsumedTopicRuleTests
{
    [Fact]
    public void Validate_WithPublishedButUnsubscribedTopic_ShouldReportError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<UnconsumedTopicRule>());

        // Assert
        Assert.Equal("raw-events", diagnostic.Target);
        Assert.Contains("no component subscribes", diagnostic.Message);
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }

    [Fact]
    public void Validate_WithSubscribedTopic_ShouldReportNothing()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<UnconsumedTopicRule>());
    }
}

public class UnproducedTopicRuleTests
{
    [Fact]
    public void Validate_WithSubscribedButUnpublishedTopic_ShouldReportError()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").Subscribes(TestTopics.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<UnproducedTopicRule>());

        // Assert
        Assert.Equal("raw-events", diagnostic.Target);
        Assert.Contains("no component publishes", diagnostic.Message);
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }

    [Fact]
    public void Validate_WithMistypedSubscription_ShouldReportBothHalvesOfTheBrokenContract()
    {
        // Arrange: the subscriber names a topic nobody publishes, orphaning the real one
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);
        s.AddLoader("loader").Subscribes(TopicRef<RawEvent>.Define("test-pubsub", "raw-eventz"));

        // Act
        var diagnostics = s.Validate();

        // Assert
        Assert.Equal("raw-events", Assert.Single(diagnostics, d => d.Message.Contains("no component subscribes")).Target);
        Assert.Equal("raw-eventz", Assert.Single(diagnostics, d => d.Message.Contains("no component publishes")).Target);
    }
}

public class NoEdgesRuleTests
{
    [Fact]
    public void Validate_WithEdgelessComponent_ShouldWarnButBuild()
    {
        // Arrange: a TI with no edges violates no completeness rule
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti");

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<NoEdgesRule>());

        // Assert
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "The component has no edges (no subscriptions, publishes, or connectors); it is likely unfinished.",
            diagnostic.Message);
        Assert.NotNull(s.Build());
    }

    [Fact]
    public void Validate_WithSubscribingComponent_ShouldReportNothing()
    {
        // Arrange: a topic subscription is an edge
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").Subscribes(TestTopics.Raw).To(TestConnectors.Erp);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor<NoEdgesRule>());
    }
}

public class PubSubConnectorNameCollisionRuleTests
{
    [Fact]
    public void Validate_WithPubSubNameEqualToDerivedComponentName_ShouldReportError()
    {
        // Arrange: the connector "shared" derives the Dapr component name "binding.shared"
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .From(ConnectorRef.Define("shared", Transport.File("./test/shared")))
            .Publishes(TopicRef<RawEvent>.Define("binding.shared", "some-topic"));

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor<PubSubConnectorNameCollisionRule>());

        // Assert
        Assert.Equal("binding.shared", diagnostic.Target);
        Assert.Contains("shared", diagnostic.Message);
    }
}

public class AllowedPatternsTests
{
    [Fact]
    public void Validate_WithMultipleDistinctConnectorsOnOneComponent_ShouldNotReportErrors()
    {
        // Arrange: a transactional integration touching two externals
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti")
            .From(TestConnectors.Pim)
            .To(TestConnectors.Erp);

        // Act & Assert
        Assert.Empty(s.Validate());
    }
}
