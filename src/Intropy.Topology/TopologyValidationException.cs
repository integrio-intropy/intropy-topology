using System.Globalization;
using System.Text;

namespace Intropy.Topology;

/// <summary>
/// Thrown by <see cref="SystemBuilder.Build"/> when the declared topology has one or
/// more error-severity violations. Carries every diagnostic found — not just the first —
/// so a SystemHost run reports the whole picture at once.
/// </summary>
public sealed class TopologyValidationException : Exception
{
    /// <summary>All diagnostics found, including warnings.</summary>
    public IReadOnlyList<TopologyDiagnostic> Diagnostics { get; }

    /// <summary>Creates the exception with no diagnostics.</summary>
    public TopologyValidationException()
    {
        Diagnostics = [];
    }

    /// <summary>Creates the exception with a message and no diagnostics.</summary>
    /// <param name="message">The exception message.</param>
    public TopologyValidationException(string message)
        : base(message)
    {
        Diagnostics = [];
    }

    /// <summary>Creates the exception with a message, an inner exception, and no diagnostics.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The causing exception.</param>
    public TopologyValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Diagnostics = [];
    }

    /// <summary>Creates the exception from the full diagnostic list.</summary>
    /// <param name="diagnostics">All diagnostics found while validating the topology.</param>
    public TopologyValidationException(IReadOnlyList<TopologyDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    private static string BuildMessage(IReadOnlyList<TopologyDiagnostic> diagnostics)
    {
        var errorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"The system topology is invalid ({errorCount} error(s)):");
        foreach (var diagnostic in diagnostics)
        {
            builder.AppendLine();
            builder.Append(CultureInfo.InvariantCulture, $"  [{diagnostic.Severity}]");
            if (diagnostic.Target is not null)
            {
                builder.Append(CultureInfo.InvariantCulture, $" {diagnostic.Target}:");
            }

            builder.Append(CultureInfo.InvariantCulture, $" {diagnostic.Message}");
        }

        return builder.ToString();
    }
}
