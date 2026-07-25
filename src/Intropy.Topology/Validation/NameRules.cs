namespace Intropy.Topology.Validation;

/// <summary>
/// Name syntax rules for topology identifiers. Short names (system, component, port,
/// endpoint, connector) must be DNS-1123 labels because they become Kubernetes resource
/// names, Dapr app-ids, or derived Dapr binding component names
/// (<c>binding.&lt;connector-name&gt;</c>); resource names (topic, pubsub, service) must be
/// DNS-1123 subdomains because they become Dapr Component <c>metadata.name</c> values.
/// </summary>
internal static class NameRules
{
    private const int MaxLabelLength = 63;
    private const int MaxSubdomainLength = 253;

    public static bool IsValidLabel(string value) =>
        value.Length is > 0 and <= MaxLabelLength && IsLabelChunk(value);

    public static bool IsValidSubdomain(string value)
    {
        if (value.Length is 0 or > MaxSubdomainLength)
        {
            return false;
        }

        foreach (var part in value.Split('.'))
        {
            if (part.Length is 0 or > MaxLabelLength || !IsLabelChunk(part))
            {
                return false;
            }
        }

        return true;
    }

    public static string RequireLabel(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (!IsValidLabel(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid name: must be a DNS-1123 label " +
                $"(lowercase alphanumerics and '-', starting and ending with an alphanumeric, at most {MaxLabelLength} characters).",
                paramName);
        }

        return value;
    }

    public static string RequireSubdomain(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (!IsValidSubdomain(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid name: must be a DNS-1123 subdomain " +
                $"(lowercase alphanumeric labels separated by '.', each label at most {MaxLabelLength} characters, " +
                $"at most {MaxSubdomainLength} characters in total).",
                paramName);
        }

        return value;
    }

    private static bool IsLabelChunk(string value)
    {
        if (!char.IsAsciiLetterLower(value[0]) && !char.IsAsciiDigit(value[0]))
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(value[^1]) && !char.IsAsciiDigit(value[^1]))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }
}
