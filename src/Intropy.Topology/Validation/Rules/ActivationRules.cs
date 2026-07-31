namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// A declared schedule must be valid five-field cron or a macro such as
/// <c>@daily</c>. The check is deliberately shallow (<see cref="CronValidator"/>) —
/// the run backend and Kubernetes are the authority on full cron semantics.
/// </summary>
internal sealed class ScheduleExpressionRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        foreach (var component in context.Builder.Components)
        {
            if (component.ScheduleCall is { } cron && !CronValidator.IsValid(cron))
            {
                yield return new TopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    $"'{cron}' is not a valid cron expression (expected five fields or a macro such as @daily).",
                    component.Name);
            }
        }
    }
}
