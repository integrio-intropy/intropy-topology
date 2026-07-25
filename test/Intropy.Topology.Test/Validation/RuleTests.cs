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

    public static IReadOnlyList<TopologyDiagnostic> DiagnosticsFor(this SystemBuilder s, string code) =>
        s.Validate().Where(d => d.Code == code).ToArray();
}

public class DuplicateComponentNameRuleTests
{
    [Fact]
    public void Validate_WithDuplicateComponentName_ShouldReportItp001()
    {
        // Arrange: two components sharing one name
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("dup").Publishes(TestTopics.Raw);
        s.AddLoader("dup").Subscribes(TestTopics.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP001"));

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("dup", diagnostic.Target);
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }
}

public class EmptySystemRuleTests
{
    [Fact]
    public void Validate_WithNoComponents_ShouldReportItp002()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");

        // Act & Assert
        Assert.Single(s.DiagnosticsFor("ITP002"));
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }
}

public class MultiplePublishesRuleTests
{
    [Fact]
    public void Validate_WithTwoPublishes_ShouldReportItp101()
    {
        // Arrange: a component publishes exactly one topic
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .Publishes(TestTopics.Raw)
            .Publishes(TestTopics.Enriched);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP101"));

        // Assert
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("extractor", diagnostic.Target);
        Assert.Equal(
            "The component declares 2 Publishes calls; a component publishes exactly one topic.",
            diagnostic.Message);
    }

    [Fact]
    public void Validate_WithSinglePublish_ShouldNotReportItp101()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor("ITP101"));
    }
}

public class DuplicateSubscriptionRuleTests
{
    [Fact]
    public void Validate_WithDuplicateSubscription_ShouldReportItp102()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader")
            .Subscribes(TestTopics.Raw)
            .Subscribes(TestTopics.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP102"));

        // Assert
        Assert.Equal("loader", diagnostic.Target);
    }
}

public class TopicContractConflictRuleTests
{
    [Fact]
    public void Validate_WithSameTopicUnderTwoContracts_ShouldReportItp103()
    {
        // Arrange: identical (pubsub, topic) declared with two contract types
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("first")
            .Publishes(TopicRef<RawEvent>.Define("test-pubsub", "shared-topic"));
        s.AddExtractor("second")
            .Publishes(TopicRef<EnrichedEvent>.Define("test-pubsub", "shared-topic"));

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP103"));

        // Assert
        Assert.Contains(typeof(RawEvent).FullName!, diagnostic.Message);
        Assert.Contains(typeof(EnrichedEvent).FullName!, diagnostic.Message);
    }
}

public class ConnectorConflictRuleTests
{
    [Fact]
    public void Validate_WithSameConnectorNameForTwoTransports_ShouldReportItp104()
    {
        // Arrange: one connector name, two different transports (different file roots)
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("first")
            .From(ConnectorRef.Define("shared", Transport.File("./test/one")))
            .Publishes(TestTopics.Raw);
        s.AddTransactionalIntegration("second")
            .From(ConnectorRef.Define("shared", Transport.File("./test/other")));

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP104"));

        // Assert
        Assert.Equal("shared", diagnostic.Target);
        Assert.Contains("conflicting transports", diagnostic.Message);
        Assert.Contains("bindings.localstorage", diagnostic.Message);
    }

    [Fact]
    public void Validate_WithSameConnectorDeclaredTwiceIdentically_ShouldNotReportItp104()
    {
        // Arrange: two usages of the same connector with the same transport
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("first")
            .From(ConnectorRef.Define("shared", Transport.File("./test/shared")))
            .Publishes(TestTopics.Raw);
        s.AddTransactionalIntegration("second")
            .From(ConnectorRef.Define("shared", Transport.File("./test/shared")));

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor("ITP104"));
    }
}

// ITP201 (invalid cron), ITP202 (missing trigger), and ITP203 (multiple TriggeredBy) are
// retired: triggers were replaced by edges and activation/workload shape moved to the
// component scaffold. ITP204/ITP205 (trigger kind / connector direction not allowed for a
// block) are compile errors — the offending methods do not exist on the block's builder
// type. ITP105/ITP106 (connector direction capability), ITP107 (unresolved connector), and
// ITP501 (API provider) retired with the minimal model.

public class MissingRequiredOutputRuleTests
{
    [Fact]
    public void Validate_WithExtractorWithoutPublishes_ShouldReportItp206()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").From(TestConnectors.Pim);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP206"));

        // Assert
        Assert.Equal("extractor", diagnostic.Target);
        Assert.Contains("Publishes", diagnostic.Message);
    }

    [Fact]
    public void Validate_WithLoaderWithoutTo_ShouldNotReportItp206()
    {
        // Arrange: a loader's destination may stay a private local component (decision 0008)
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").Subscribes(TestTopics.Raw);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor("ITP206"));
    }

    [Fact]
    public void Validate_WithTransactionalIntegrationWithoutConnectors_ShouldNotReportItp206()
    {
        // Arrange: TI has no required output; an empty one is only an ITP302 warning
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti");

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor("ITP206"));
    }
}

public class MissingRequiredSubscriptionRuleTests
{
    [Fact]
    public void Validate_WithLoaderSubscribingToTwoTopics_ShouldReportItp207()
    {
        // Arrange: a loader consumes exactly one topic
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader")
            .Subscribes(TestTopics.Raw)
            .Subscribes(TestTopics.Enriched)
            .To(TestConnectors.Erp);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP207"));

        // Assert
        Assert.Contains("exactly one topic", diagnostic.Message);
    }

    [Fact]
    public void Validate_WithLoaderWithoutSubscription_ShouldReportItp207()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").To(TestConnectors.Erp);

        // Act & Assert
        Assert.Single(s.DiagnosticsFor("ITP207"));
    }
}

public class UnconsumedTopicRuleTests
{
    [Fact]
    public void Validate_WithPublishedButUnsubscribedTopic_ShouldReportItp208()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor").Publishes(TestTopics.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP208"));

        // Assert
        Assert.Equal("raw-events", diagnostic.Target);
        Assert.Contains("no component subscribes", diagnostic.Message);
        Assert.Throws<TopologyValidationException>(() => s.Build());
    }

    [Fact]
    public void Validate_WithSubscribedTopic_ShouldNotReportItp208()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system").WithValidComponent();

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor("ITP208"));
    }
}

public class UnproducedTopicRuleTests
{
    [Fact]
    public void Validate_WithSubscribedButUnpublishedTopic_ShouldReportItp209()
    {
        // Arrange
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").Subscribes(TestTopics.Raw);

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP209"));

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
        Assert.Equal("raw-events", Assert.Single(diagnostics, d => d.Code == "ITP208").Target);
        Assert.Equal("raw-eventz", Assert.Single(diagnostics, d => d.Code == "ITP209").Target);
    }
}

public class NoEdgesRuleTests
{
    [Fact]
    public void Validate_WithEdgelessComponent_ShouldWarnItp302ButBuild()
    {
        // Arrange: a TI with no edges violates no completeness rule
        var s = SystemBuilder.Create("test-system");
        s.AddTransactionalIntegration("ti");

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP302"));

        // Assert
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "The component has no edges (no subscriptions, publishes, or connectors); it is likely unfinished.",
            diagnostic.Message);
        Assert.NotNull(s.Build());
    }

    [Fact]
    public void Validate_WithSubscribingComponent_ShouldNotWarnItp302()
    {
        // Arrange: a topic subscription is an edge
        var s = SystemBuilder.Create("test-system");
        s.AddLoader("loader").Subscribes(TestTopics.Raw).To(TestConnectors.Erp);

        // Act & Assert
        Assert.Empty(s.DiagnosticsFor("ITP302"));
    }
}

public class PubSubConnectorNameCollisionRuleTests
{
    [Fact]
    public void Validate_WithPubSubNameEqualToDerivedComponentName_ShouldReportItp401()
    {
        // Arrange: the connector "shared" derives the Dapr component name "binding.shared"
        var s = SystemBuilder.Create("test-system");
        s.AddExtractor("extractor")
            .From(ConnectorRef.Define("shared", Transport.File("./test/shared")))
            .Publishes(TopicRef<RawEvent>.Define("binding.shared", "some-topic"));

        // Act
        var diagnostic = Assert.Single(s.DiagnosticsFor("ITP401"));

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
