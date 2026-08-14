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

public static class TestPorts
{
    public static readonly PortRef Pim =
        PortRef.Define("pim");

    public static readonly PortRef Erp =
        PortRef.Define("erp");
}
