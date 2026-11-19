using Intropy.Topology.Model;

namespace Intropy.Topology.Validation.Rules;

/// <summary>
/// A port has one kind: declared with <see cref="PortRef.Define"/> in one place and
/// <see cref="PortRef.DefineExternal"/> in another, it would render a binding for some components
/// and not for others. The model keeps the first-seen kind, so the conflict is visible only in
/// the raw <see cref="PortRef"/> declarations.
/// </summary>
internal sealed class PortKindConflictRule : IDeclarationRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemBuilder builder)
    {
        var conflicts = builder.Components
            .SelectMany(c => c.PortCalls.Select(p => p.Port))
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Select(p => p.Kind).Distinct().Count() > 1);

        foreach (var group in conflicts)
        {
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"The port '{group.Key}' is declared both as a binding port and as an external port; declare it once, with PortRef.Define or PortRef.DefineExternal.",
                group.Key);
        }
    }
}

/// <summary>
/// A cursor store is a Dapr Component in the system's namespace, like a pubsub and a port's
/// binding, so its minted name must not equal either. Nor may it equal a service's app id: a
/// service mock renders to the same <c>components/&lt;name&gt;.yaml</c> path.
/// </summary>
internal sealed class CursorStoreNameCollisionRule : IModelRule
{
    public IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology)
    {
        var taken = topology.Topics.Select(t => t.PubSubName)
            .Concat(topology.Components.Select(c => c.InternalQueue?.PubSubName).OfType<string>())
            .Concat(topology.Ports.Select(p => p.DaprComponentName))
            .Concat(topology.Services.Select(s => s.AppId))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var component in topology.Components.Where(c => c.CursorStore is not null && taken.Contains(c.CursorStore)))
        {
            yield return new TopologyDiagnostic(
                DiagnosticSeverity.Error,
                $"The cursor store '{component.CursorStore}' minted for component '{component.Name}' has the same name as a pubsub, port, or service; all become Dapr Component names in the same namespace.",
                component.CursorStore);
        }
    }
}
