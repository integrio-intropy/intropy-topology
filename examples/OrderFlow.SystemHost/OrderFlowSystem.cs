using Intropy.Topology;

/// <summary>The order-flow system: what exists, and what connects it.</summary>
public sealed class OrderFlowSystem : ISystemDefinition
{
    public string SystemName => "order-flow";

    public void Define(SystemBuilder builder)
    {
        // WithSchedule runs ahead of the `intropy sys create` golden template on purpose:
        // CLI codegen does not emit it yet. The extractor runs once at host start and then
        // on every tick; drop "* * * * *" to return to the run-once behavior.
        builder.AddExtractor("order-extractor")
            .From(Connectors.OrderExtractorSource)
            .Publishes(Topics.Orders)
            .WithSchedule("* * * * *");
        builder.AddLoader("order-loader").Subscribes(Topics.Orders).To(Connectors.OrderLoaderDestination);

        // UsesService also runs ahead of the golden template: the whole system waits for
        // the shared idempotency store before any component starts.
        builder.UsesService(Services.Idempotency);
    }
}
