using Intropy.Topology.Model;

namespace Intropy.Topology.Validation;

/// <summary>Runs every topology rule and collects the diagnostics.</summary>
internal static class TopologyRules
{
    private static readonly IModelRule[] ModelRules =
    [
        new Rules.DuplicateComponentNameRule(),
        new Rules.EmptySystemRule(),
        new Rules.DuplicatePortRule(),
        new Rules.DuplicateSubscriptionRule(),
        new Rules.MissingRequiredOutputRule(),
        new Rules.MissingRequiredSubscriptionRule(),
        new Rules.UnconsumedTopicRule(),
        new Rules.UnproducedTopicRule(),
        new Rules.NoEdgesRule(),
        new Rules.PubSubConnectorNameCollisionRule(),
        new Rules.ServiceAppIdCollisionRule(),
    ];

    private static readonly IDeclarationRule[] DeclarationRules =
    [
        new Rules.TopicContractConflictRule(),
        new Rules.DuplicateServiceUsageRule(),
    ];

    public static IReadOnlyList<TopologyDiagnostic> Run(SystemBuilder builder, SystemTopology topology)
    {
        return
        [
            .. ModelRules.SelectMany(rule => rule.Evaluate(topology)),
            .. DeclarationRules.SelectMany(rule => rule.Evaluate(builder)),
        ];
    }
}
