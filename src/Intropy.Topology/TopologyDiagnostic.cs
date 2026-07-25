namespace Intropy.Topology;

/// <summary>How severe a topology diagnostic is.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Suspicious but does not fail <see cref="SystemBuilder.Build"/>.</summary>
    Warning,

    /// <summary>Fails <see cref="SystemBuilder.Build"/>.</summary>
    Error,
}

/// <summary>One validation finding produced while building a topology.</summary>
/// <param name="Code">The stable rule code (e.g. <c>ITP101</c>).</param>
/// <param name="Severity">The finding's severity.</param>
/// <param name="Message">A human-readable description of the violation.</param>
/// <param name="Target">The component or resource name the finding is about, when applicable.</param>
public sealed record TopologyDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? Target);
