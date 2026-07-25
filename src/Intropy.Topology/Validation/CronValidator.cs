namespace Intropy.Topology.Validation;

/// <summary>
/// Shallow syntax check for Kubernetes CronJob schedule expressions: the known
/// <c>@daily</c>-style macros, or exactly five whitespace-separated fields of cron
/// characters. Kubernetes is the authority on full cron semantics — this only catches
/// obvious typos at Build time.
/// </summary>
internal static class CronValidator
{
    private static readonly string[] Macros =
        ["@hourly", "@daily", "@weekly", "@monthly", "@yearly", "@annually", "@midnight"];

    public static bool IsValid(string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.StartsWith('@'))
        {
            return Macros.Contains(trimmed, StringComparer.OrdinalIgnoreCase);
        }

        var fields = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length == 5
            && fields.All(f => f.All(c => char.IsAsciiLetterOrDigit(c) || c is '*' or ',' or '/' or '-'));
    }
}
