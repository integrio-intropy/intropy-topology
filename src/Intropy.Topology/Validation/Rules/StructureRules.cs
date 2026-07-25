namespace Intropy.Topology.Validation.Rules;

/// <summary>ITP001: every component needs a unique name.</summary>
internal sealed class DuplicateComponentNameRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        var duplicates = context.Topology.Components
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicates)
        {
            yield return new TopologyDiagnostic(
                "ITP001",
                DiagnosticSeverity.Error,
                $"The component name '{group.Key}' is declared {group.Count()} times; component names must be unique.",
                group.Key);
        }
    }
}

/// <summary>ITP002: a system must declare at least one component.</summary>
internal sealed class EmptySystemRule : ITopologyRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context)
    {
        if (context.Topology.Components.Count == 0)
        {
            yield return new TopologyDiagnostic(
                "ITP002",
                DiagnosticSeverity.Error,
                "The system declares no components.",
                context.Topology.SystemName);
        }
    }
}
