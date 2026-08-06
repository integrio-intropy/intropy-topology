using Intropy.Topology.Model;

namespace Intropy.Topology.Validation;

/// <summary>Runs every topology rule and collects the diagnostics.</summary>
internal static class TopologyRules
{
    private static readonly ITopologyRule[] Rules =
    [
        new Rules.DuplicateComponentNameRule(),
        new Rules.EmptySystemRule(),
        new Rules.DuplicatePortRule(),
        new Rules.DuplicateSubscriptionRule(),
        new Rules.TopicContractConflictRule(),
        new Rules.MissingRequiredOutputRule(),
        new Rules.MissingRequiredSubscriptionRule(),
        new Rules.UnconsumedTopicRule(),
        new Rules.UnproducedTopicRule(),
        new Rules.NoEdgesRule(),
        new Rules.PubSubConnectorNameCollisionRule(),
        new Rules.ServiceUsageRule(),
    ];

    public static IReadOnlyList<TopologyDiagnostic> Run(SystemBuilder builder, SystemTopology topology)
    {
        var context = new ValidationContext(builder, topology);
        return Rules.SelectMany(rule => rule.Evaluate(context)).ToArray();
    }
}
