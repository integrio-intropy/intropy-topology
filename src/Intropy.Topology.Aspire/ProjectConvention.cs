namespace Intropy.Topology.Aspire;

/// <summary>
/// Locates a component's integration by folder convention: a component named
/// <c>order-extractor</c> lives in the sibling folder <c>../order-extractor/</c>, relative to
/// the AppHost. The project is <c>src/*.csproj</c> inside it (the scaffold layout), falling
/// back to a flat <c>order-extractor.csproj</c>; its local Dapr components live in
/// <c>local/dapr-components/</c>.
/// </summary>
internal static class ProjectConvention
{
    /// <summary>The integration's root folder: the sibling folder named after the component.</summary>
    public static string IntegrationRoot(string appHostDirectory, string componentName) =>
        Path.GetFullPath(Path.Combine(appHostDirectory, "..", componentName));

    /// <summary>The project that runs the component: <c>src/*.csproj</c>, else <c>{name}.csproj</c> flat.</summary>
    public static string Resolve(string appHostDirectory, string componentName)
    {
        var root = IntegrationRoot(appHostDirectory, componentName);
        var srcDirectory = Path.Combine(root, "src");
        if (Directory.Exists(srcDirectory) &&
            Directory.GetFiles(srcDirectory, "*.csproj") is [var project])
        {
            return project;
        }

        return Path.Combine(root, componentName + ".csproj");
    }

    /// <summary>The integration's own Dapr components — the set it runs on standalone.</summary>
    public static string LocalComponentsDir(string appHostDirectory, string componentName) =>
        Path.Combine(IntegrationRoot(appHostDirectory, componentName), "local", "dapr-components");
}
