using Intropy.Topology;
using Intropy.Topology.Generation;

namespace Intropy.Topology.WipSystem;

public sealed record WipOrder(string OrderNumber);

/// <summary>
/// A work-in-progress system: the published topic has no subscriber yet. Validation warns;
/// the system must still materialize for read-only inspection.
/// </summary>
public sealed class WipSystemDefinition : ISystemDefinition
{
    public static readonly TopicRef<WipOrder> Raw = TopicRef<WipOrder>.Define("pubsub-a", "wip-raw");
    public static readonly PortRef Placeholder = PortRef.Define("placeholder");

    public string SystemName => "wip-system";

    public void Define(SystemBuilder builder)
    {
        builder.AddExtractor("wip-extractor").From(Placeholder).Publishes(Raw);
    }
}

/// <summary>The local file resolution backing <see cref="WipSystemDefinition"/>'s port.</summary>
public sealed class WipDevelopment : IDevelopmentDefinition
{
    public void Define(DevelopmentBuilder development)
    {
        development.Files(WipSystemDefinition.Placeholder).RootPath("./test/placeholder");
    }
}
