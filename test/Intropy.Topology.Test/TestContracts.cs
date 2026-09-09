namespace Intropy.Topology.Test;

public sealed record RawEvent;

public sealed record EnrichedEvent;

public static class TestMessages
{
    public static readonly MessageRef<RawEvent> Raw =
        MessageRef<RawEvent>.Define("raw-events", "test-pubsub");

    public static readonly MessageRef<EnrichedEvent> Enriched =
        MessageRef<EnrichedEvent>.Define("enriched-events", "test-pubsub");
}

public static class TestPorts
{
    public static readonly PortRef Pim =
        PortRef.Define("pim");

    public static readonly PortRef Erp =
        PortRef.Define("erp");
}
