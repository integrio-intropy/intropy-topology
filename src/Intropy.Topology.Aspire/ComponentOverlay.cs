namespace Intropy.Topology.Aspire;

/// <summary>
/// Stages the effective Dapr component set for one integration: everything from its own
/// <c>local/dapr-components/</c> passes through, then the system's generated components overlay
/// it — a generated component with the same <c>metadata.name</c> replaces the local one, new
/// names are added. The staged set lands at a stable, inspectable path under the AppHost's
/// <c>obj/dapr-components/&lt;component&gt;/</c> and is regenerated on every start; the
/// integration's sidecar gets it as its resources path. Standalone runs load the local
/// directory instead — same names, different backing.
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

        // metadata.name -> staged file, so a generated component can replace its local counterpart.
        var stagedByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in ComponentFiles(ProjectConvention.LocalComponentsDir(appHostDirectory, componentName)))
        {
            var target = Path.Combine(stagedDir, Path.GetFileName(file));
            File.Copy(file, target);
            if (MetadataName(File.ReadAllText(file)) is { } name)
            {
                stagedByName[name] = target;
            }
        }

        foreach (var file in ComponentFiles(generatedComponentsDir))
        {
            var name = MetadataName(File.ReadAllText(file));
            if (name is not null && stagedByName.TryGetValue(name, out var replaced))
            {
                File.Delete(replaced);
            }

            var target = Path.Combine(stagedDir, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
            if (name is not null)
            {
                stagedByName[name] = target;
            }
        }

        return stagedDir;
    }

    /// <summary>
    /// The top-level <c>metadata.name</c> of a Dapr resource YAML. Only the unindented
    /// <c>metadata:</c> block qualifies — <c>spec.metadata</c> entries are indented past it.
    /// </summary>
    internal static string? MetadataName(string yaml)
    {
        var lines = yaml.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() != "metadata:")
            {
                continue;
            }

            for (var j = i + 1; j < lines.Length && lines[j].StartsWith(' '); j++)
            {
                var entry = lines[j].Trim();
                if (entry.StartsWith("name:", StringComparison.Ordinal))
                {
                    return entry["name:".Length..].Trim().Trim('"', '\'');
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ComponentFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory)
                .Where(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal)
            : [];
}
