namespace Intropy.Topology.Test;

public sealed record RawEvent;

public sealed record EnrichedEvent;

public static class TestTopics
{
    public static readonly TopicRef<RawEvent> Raw =
        TopicRef<RawEvent>.Define("test-pubsub", "raw-events");

    public static readonly TopicRef<EnrichedEvent> Enriched =
        TopicRef<EnrichedEvent>.Define("test-pubsub", "enriched-events");
}

public static class TestConnectors
{
    public static readonly ConnectorRef Pim =
        ConnectorRef.Define("pim", Transport.File("./test/pim"));

    public static readonly ConnectorRef Erp =
        ConnectorRef.Define("erp", Transport.File("./test/erp"));
}
