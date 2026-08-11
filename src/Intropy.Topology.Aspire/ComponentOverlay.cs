namespace Intropy.Topology.Aspire;

/// <summary>
/// Stages the effective Dapr resource set for one integration. Local resources are copied first;
/// generated resources replace only an identical <c>(apiVersion, kind, metadata.name)</c> identity.
/// A generated HTTP endpoint is staged only for a component named in its scopes.
/// </summary>
internal static class ComponentOverlay
{
    /// <summary>Stages the component set and returns the staged directory.</summary>
    public static string Stage(string appHostDirectory, string componentName, string generatedComponentsDir)
    {
        var stagedDir = Path.Combine(appHostDirectory, "obj", "dapr-components", componentName);
        if (Directory.Exists(stagedDir))
        {
            Directory.Delete(stagedDir, recursive: true);
        }

        Directory.CreateDirectory(stagedDir);
        var stagedByName = new Dictionary<string, StagedResource>(StringComparer.Ordinal);
        CopyResources(ProjectConvention.LocalComponentsDir(appHostDirectory, componentName), stagedDir, stagedByName, componentName, generated: false);
        CopyResources(generatedComponentsDir, stagedDir, stagedByName, componentName, generated: true);
        return stagedDir;
    }

    /// <summary>The top-level <c>metadata.name</c> of a single-document Dapr resource YAML.</summary>
    internal static string? MetadataName(string yaml) => Parse(yaml, "resource")?.Identity.Name;

    private static void CopyResources(
        string sourceDirectory,
        string stagedDirectory,
        Dictionary<string, StagedResource> stagedByName,
        string componentName,
        bool generated)
    {
        foreach (var file in ComponentFiles(sourceDirectory))
        {
            var yaml = File.ReadAllText(file);
            var resource = Parse(yaml, file) ?? throw new InvalidOperationException($"Dapr resource '{file}' has no metadata.name.");
            if (generated && resource.IsHttpEndpoint && !resource.Scopes.Contains(componentName, StringComparer.Ordinal))
            {
                continue;
            }

            if (stagedByName.TryGetValue(resource.Identity.Name, out var existing))
            {
                if (existing.IsGenerated == generated
                    || existing.Identity.ApiVersion != resource.Identity.ApiVersion
                    || existing.Identity.Kind != resource.Identity.Kind
                    || existing.Identity.Name != resource.Identity.Name)
                {
                    throw new InvalidOperationException(
                        $"Dapr resource '{file}' conflicts with an existing resource named '{resource.Identity.Name}'; replacement requires a unique matching apiVersion, kind, and metadata.name.");
                }

                var prior = Path.Combine(stagedDirectory, existing.Identity.FileName);
                File.Delete(prior);
            }

            var targetName = Path.GetFileName(file);
            var target = Path.Combine(stagedDirectory, targetName);
            File.Copy(file, target, overwrite: true);
            stagedByName[resource.Identity.Name] = new StagedResource(resource.Identity with { FileName = targetName }, generated);
        }
    }

    /// <summary>
    /// Line-oriented reader for the Dapr resource YAML this overlay accepts. Supported
    /// subset, deliberately narrow because both writers (the generator and scaffolded
    /// local components) produce it: block style only; one document per file;
    /// <c>apiVersion</c>, <c>kind</c>, and <c>metadata:</c>/<c>scopes:</c> as top-level keys at
    /// column 0; scalar values optionally single- or double-quoted; <c>scopes</c> entries as
    /// <c>- value</c> list items. Anchors, aliases, flow collections, multi-line values, and
    /// comments are NOT part of the contract — a <c>name:</c> nested under another top-level
    /// mapping is not recognized as <c>metadata.name</c> (only lines while inside the
    /// <c>metadata:</c> block count). Reject or extend consciously: staging behavior depends
    /// on the identity extracted here.
    /// </summary>
    private static Resource? Parse(string yaml, string source)
    {
        if (yaml.Split("---", StringSplitOptions.None).Length > 1)
        {
            throw new InvalidOperationException($"Dapr resource '{source}' contains multiple YAML documents.");
        }

        string? apiVersion = null;
        string? kind = null;
        string? name = null;
        var scopes = new List<string>();
        var inMetadata = false;
        var inScopes = false;
        foreach (var raw in yaml.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("apiVersion:", StringComparison.Ordinal)) apiVersion = Value(line);
            if (line.StartsWith("kind:", StringComparison.Ordinal)) kind = Value(line);
            if (line == "metadata:") { inMetadata = true; inScopes = false; continue; }
            if (line == "scopes:") { inScopes = true; inMetadata = false; continue; }
            if (inMetadata && raw.StartsWith(' ') && line.TrimStart().StartsWith("name:", StringComparison.Ordinal)) name = Value(line.TrimStart());
            if (inScopes && line.TrimStart().StartsWith("- ", StringComparison.Ordinal)) scopes.Add(Value(line.TrimStart()[2..]));
            if (!raw.StartsWith(' ') && !line.TrimStart().StartsWith("- ", StringComparison.Ordinal)
                && line.Length > 0 && line is not "metadata:" and not "scopes:") { inMetadata = false; inScopes = false; }
        }

        return name is null || apiVersion is null || kind is null
            ? null
            : new Resource(new ResourceIdentity(apiVersion, kind, name, Path.GetFileName(source)), scopes);
    }

    private static string Value(string line) => line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim().Trim('"', '\'');

    private static IEnumerable<string> ComponentFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory)
                .Where(file => file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.Ordinal)
            : [];

    private sealed record StagedResource(ResourceIdentity Identity, bool IsGenerated);

    private sealed record Resource(ResourceIdentity Identity, IReadOnlyList<string> Scopes)
    {
        public bool IsHttpEndpoint => Identity.Kind == "HTTPEndpoint";
    }

    private sealed record ResourceIdentity(string ApiVersion, string Kind, string Name, string FileName);
}
