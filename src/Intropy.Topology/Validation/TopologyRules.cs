using Intropy.Topology.Model;

namespace Intropy.Topology.Validation;

/// <summary>Runs every topology rule and collects the diagnostics.</summary>
internal static class TopologyRules
{
    // Retired codes — do not reuse. Enforced at compile time by the block builders:
    // ITP204 (trigger kind not allowed for the block kind), ITP205 (connector direction
    // not allowed for the block kind). No longer meaningful since triggers were replaced
    // by edges and activation/workload shape moved to the component scaffold: ITP201
    // (invalid cron on the trigger-era ScheduleTrigger — its successor for the schedule
    // *attribute* introduced by decision 0010 is ITP210), ITP202 (missing trigger),
    // ITP203 (multiple TriggeredBy calls).
    // Retired with the minimal model (full versions live on the full-topology branch):
    // ITP105/ITP106 (connector direction capability — the file transport supports both),
    // ITP107 (unresolved connector — every connector now declares its transport),
    // ITP501 (API provider — the API surface is not part of the minimal model).
    // ITP301 (service declared but unused) is retired permanently: it presupposed the
    // per-component .Uses edge; services are system-level since ADR 0011, so there is
    // no usage to check. ITP402 was revived by the same ADR with its original meaning
    // (service name collision).
    private static readonly ITopologyRule[] Rules =
    [
        new Rules.DuplicateComponentNameRule(),
        new Rules.EmptySystemRule(),
        new Rules.DuplicatePortRule(),
        new Rules.DuplicateSubscriptionRule(),
        new Rules.TopicContractConflictRule(),
        new Rules.ConnectorConflictRule(),
        new Rules.ServiceConflictRule(),
        new Rules.MissingRequiredOutputRule(),
        new Rules.MissingRequiredSubscriptionRule(),
        new Rules.ScheduleExpressionRule(),
        new Rules.UnconsumedTopicRule(),
        new Rules.UnproducedTopicRule(),
        new Rules.NoEdgesRule(),
        new Rules.PubSubConnectorNameCollisionRule(),
        new Rules.ServiceNameCollisionRule(),
    ];

    public static IReadOnlyList<TopologyDiagnostic> Run(SystemBuilder builder, SystemTopology topology)
    {
        var context = new ValidationContext(builder, topology);
        return Rules.SelectMany(rule => rule.Evaluate(context)).ToArray();
    }
}
