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

public class PubSubConnectorNameCollisionRuleTests
{
    [Fact]
    public void Validate_WithPubSubNameEqualToDerivedComponentName_ShouldReportError()
    {
        // Arrange: the connector "shared" derives the Dapr component name "binding.shared"
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .From(ConnectorRef.Define("shared"))
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
