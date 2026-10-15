using System.Reflection;
using System.Text.RegularExpressions;
using Intropy.Topology;
using Intropy.Topology.Model;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace Intropy.Topology.Generation;

/// <summary>Declares local development substitutions for a SystemHost topology.</summary>
public interface IDevelopmentDefinition
{
    /// <summary>Records local development substitutions.</summary>
    /// <param name="development">The builder scoped to the validated system topology.</param>
    void Define(DevelopmentBuilder development);
}

/// <summary>Records local development substitutions: service mocks, port file resolutions, and re-run cadences. Not thread-safe.</summary>
public sealed class DevelopmentBuilder
{
    private readonly SystemTopology _topology;
    private readonly string _root;
    private readonly List<MockDeclaration> _mocks = [];
    private readonly List<FileDeclaration> _files = [];
    private readonly List<RerunDeclaration> _reruns = [];

    internal DevelopmentBuilder(SystemTopology topology, string root)
    {
        _topology = topology;
        _root = Path.GetFullPath(root);
    }

    /// <summary>Declares a local mock for a service used by this topology.</summary>
    /// <param name="service">The platform-service identity to resolve locally.</param>
    public MockBuilder Mock(ServiceRef service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (!_topology.Services.Any(candidate => candidate.AppId == service.AppId))
        {
            throw new DevelopmentValidationException($"Service '{service.AppId}' is not used by the system topology.");
        }

        if (_mocks.Any(candidate => candidate.AppId == service.AppId))
        {
            throw new DevelopmentValidationException($"Service '{service.AppId}' has more than one mock declaration.");
        }

        var declaration = new MockDeclaration(service.AppId);
        _mocks.Add(declaration);
        return new MockBuilder(declaration);
    }

    /// <summary>Declares the local file resolution for a port used by this topology.</summary>
    /// <param name="port">The port to resolve locally.</param>
    public FileBuilder Files(PortRef port)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (!_topology.Ports.Any(candidate => candidate.Name == port.Name))
        {
            throw new DevelopmentValidationException($"Port '{port.Name}' is not used by the system topology.");
        }

        if (_files.Any(candidate => candidate.PortName == port.Name))
        {
            throw new DevelopmentValidationException($"Port '{port.Name}' has more than one file resolution.");
        }

        var declaration = new FileDeclaration(port.Name);
        _files.Add(declaration);
        return new FileBuilder(declaration);
    }

    /// <summary>Declares how often a run-to-completion component is re-run locally.</summary>
    /// <param name="componentName">The name of an extractor or transactional integration in the topology.</param>
    public RerunBuilder Rerun(string componentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        var component = _topology.Components.SingleOrDefault(candidate => candidate.Name == componentName);
        if (component is null)
        {
            throw new DevelopmentValidationException($"Component '{componentName}' is not declared by the system topology.");
        }

        if (!component.Kind.IsRunToCompletion())
        {
            throw new DevelopmentValidationException(
                $"Component '{componentName}' is a {component.Kind} and stays resident; only run-to-completion components (extractors and transactional integrations) can be re-run.");
        }

        if (_reruns.Any(candidate => candidate.ComponentName == componentName))
        {
            throw new DevelopmentValidationException($"Component '{componentName}' has more than one re-run declaration.");
        }

        var declaration = new RerunDeclaration(componentName);
        _reruns.Add(declaration);
        return new RerunBuilder(declaration);
    }

    internal DevelopmentManifest Build()
    {
        var mocks = _mocks.Select(mock => OpenApiArtifact.Inspect(_root, mock)).ToArray();
        var duplicateIdentity = mocks.GroupBy(mock => (mock.Title, mock.Version))
            .FirstOrDefault(group => group.Select(mock => mock.AppId).Distinct(StringComparer.Ordinal).Count() > 1);
        if (duplicateIdentity is not null)
        {
            throw new DevelopmentValidationException(
                $"Mocks for services '{string.Join("', '", duplicateIdentity.Select(mock => mock.AppId))}' resolve to the same Microcks identity '{duplicateIdentity.Key.Title}' version '{duplicateIdentity.Key.Version}'.");
        }

        var unresolved = _topology.Ports
            .Where(port => !_files.Any(file => file.PortName == port.Name))
            .Select(port => port.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unresolved.Length > 0)
        {
            throw new DevelopmentValidationException(
                $"Ports '{string.Join("', '", unresolved)}' have no local file resolution.");
        }

        var files = _files.Select(file => FileRootPath.Inspect(_root, file)).ToArray();
        var undeclared = _reruns
            .Where(rerun => rerun.Delay is null)
            .Select(rerun => rerun.ComponentName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (undeclared.Length > 0)
        {
            throw new DevelopmentValidationException(
                $"Re-run declarations for components '{string.Join("', '", undeclared)}' do not declare a delay.");
        }

        return new DevelopmentManifest(
            mocks.OrderBy(mock => mock.AppId, StringComparer.Ordinal).ToArray(),
            files.OrderBy(file => file.PortName, StringComparer.Ordinal).ToArray(),
            _reruns.Select(rerun => new ComponentRerun(rerun.ComponentName, rerun.Delay!.Value))
                .OrderBy(rerun => rerun.ComponentName, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Configures the OpenAPI artifact for one service mock.</summary>
    public sealed class MockBuilder
    {
        private readonly MockDeclaration _declaration;
        internal MockBuilder(MockDeclaration declaration) => _declaration = declaration;

        /// <summary>Uses a self-contained OpenAPI 3.0.x document relative to the SystemHost directory.</summary>
        /// <param name="path">The relative contract path.</param>
        public MockBuilder FromOpenApi(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (_declaration.Path is not null)
            {
                throw new DevelopmentValidationException($"Service '{_declaration.AppId}' has more than one OpenAPI artifact.");
            }

            _declaration.Path = path;
            return this;
        }
    }

    /// <summary>Configures the local root path for one port file resolution.</summary>
    public sealed class FileBuilder
    {
        private readonly FileDeclaration _declaration;
        internal FileBuilder(FileDeclaration declaration) => _declaration = declaration;

        /// <summary>Reads and writes files in a folder relative to the SystemHost directory.</summary>
        /// <param name="path">The relative folder path.</param>
        public FileBuilder RootPath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (_declaration.Path is not null)
            {
                throw new DevelopmentValidationException($"Port '{_declaration.PortName}' has more than one root path.");
            }

            _declaration.Path = path;
            return this;
        }
    }

    internal sealed class MockDeclaration(string appId)
    {
        public string AppId { get; } = appId;
        public string? Path { get; set; }
    }

    /// <summary>Configures the local re-run cadence for one run-to-completion component.</summary>
    public sealed class RerunBuilder
    {
        private readonly RerunDeclaration _declaration;
        internal RerunBuilder(RerunDeclaration declaration) => _declaration = declaration;

        /// <summary>Re-runs the component again once <paramref name="delay"/> has passed after a run completes; the delay starts when the run finishes, not when it starts.</summary>
        /// <param name="delay">The wait between a run's completion and the next run's start; must be positive.</param>
        /// <remarks>The deployed schedule lives in deployment configuration; this delay is local host mechanism only and never appears in generated artifacts.</remarks>
        public RerunBuilder AfterEachRun(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new DevelopmentValidationException(
                    $"Re-run delay for component '{_declaration.ComponentName}' must be positive.");
            }

            if (_declaration.Delay is not null)
            {
                throw new DevelopmentValidationException(
                    $"Component '{_declaration.ComponentName}' has more than one re-run delay.");
            }

            _declaration.Delay = delay;
            return this;
        }
    }

    internal sealed class FileDeclaration(string portName)
    {
        public string PortName { get; } = portName;
        public string? Path { get; set; }
    }

    internal sealed class RerunDeclaration(string componentName)
    {
        public string ComponentName { get; } = componentName;
        public TimeSpan? Delay { get; set; }
    }
}

/// <summary>Validated development substitutions consumed by generation and local runtime backends.</summary>
public sealed record DevelopmentManifest(
    IReadOnlyList<OpenApiMock> Mocks,
    IReadOnlyList<PortFileResolution> Files,
    IReadOnlyList<ComponentRerun> Reruns);

/// <summary>
/// One run-to-completion component's local re-run cadence. Local-only host mechanism: it is
/// never written into the generated artifacts, so it cannot drift with the deployed schedule
/// that deployment configuration owns.
/// </summary>
/// <param name="ComponentName">The component to re-run.</param>
/// <param name="Delay">The wait between a local run's completion and its next start.</param>
public sealed record ComponentRerun(string ComponentName, TimeSpan Delay);

/// <summary>One port's validated local file resolution.</summary>
/// <param name="PortName">The resolved port.</param>
/// <param name="RootPath">The folder path relative to the SystemHost directory.</param>
public sealed record PortFileResolution(string PortName, string RootPath);

/// <summary>One OpenAPI-backed platform-service mock with a verified Microcks identity.</summary>
public sealed record OpenApiMock(string AppId, string ArtifactPath, string Title, string Version);

/// <summary>Thrown when a development definition or its artifacts are invalid.</summary>
public sealed class DevelopmentValidationException : IntropyException
{
    /// <summary>Creates an empty validation exception.</summary>
    public DevelopmentValidationException() { }

    /// <summary>Creates a validation exception with a focused diagnostic.</summary>
    public DevelopmentValidationException(string message) : base(message) { }

    /// <summary>Creates a validation exception with its underlying cause.</summary>
    public DevelopmentValidationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Discovers the optional development definition associated with a SystemHost assembly.</summary>
public static class DevelopmentDiscovery
{
    /// <summary>Discovers and validates zero or one development definition.</summary>
    public static DevelopmentManifest Discover(Assembly assembly, SystemTopology topology, string root)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var result = SingleImplementation.Find<IDevelopmentDefinition>(assembly.GetTypes(), requireOne: false);
        if (result.Error is not null)
        {
            throw new DevelopmentValidationException(result.Error + ".");
        }

        if (result.Instance is null)
        {
            return new DevelopmentManifest([], [], []);
        }

        var builder = new DevelopmentBuilder(topology, root);
        result.Instance.Define(builder);
        return builder.Build();
    }
}

internal static class FileRootPath
{
    public static PortFileResolution Inspect(string root, DevelopmentBuilder.FileDeclaration declaration)
    {
        var portName = declaration.PortName;
        var relativePath = declaration.Path;
        if (relativePath is null)
        {
            throw new DevelopmentValidationException($"File resolution for port '{portName}' does not declare a root path.");
        }

        var rootPath = PathContainment.ResolveRoot(root);
        var folderPath = PathContainment.Resolve(rootPath, relativePath, out var escaped);
        var error = $"File resolution '{relativePath}' for port '{portName}'";
        if (Directory.Exists(folderPath))
        {
            folderPath = PathContainment.ResolveDirectory(folderPath, $"{error} cannot be resolved");
        }

        if (escaped || !PathContainment.IsWithin(rootPath, folderPath))
        {
            throw new DevelopmentValidationException($"{error} escapes the SystemHost directory.");
        }

        return new PortFileResolution(portName, Path.GetRelativePath(rootPath, folderPath));
    }
}

/// <summary>
/// Canonicalizes paths against the SystemHost directory and rejects escapes. Both
/// development artifact kinds (port file roots, OpenAPI mock documents) resolve
/// the same way: the root is canonicalized through its symlink target, the relative
/// path is combined and canonicalized, and any path landing outside the root is a
/// validation error.
/// </summary>
internal static class PathContainment
{
    /// <summary>The canonical SystemHost root; symlink targets are resolved.</summary>
    public static string ResolveRoot(string root) =>
        ResolveDirectory(Path.GetFullPath(root), $"SystemHost directory '{root}' cannot be resolved");

    /// <summary>Canonicalizes a directory through its symlink target.</summary>
    /// <exception cref="DevelopmentValidationException">The path cannot be resolved.</exception>
    public static string ResolveDirectory(string path, string error)
    {
        try
        {
            return Path.GetFullPath(new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path);
        }
        catch (IOException ex)
        {
            throw new DevelopmentValidationException($"{error}: {ex.Message}", ex);
        }
    }

    /// <summary>Combines and canonicalizes <paramref name="relativePath"/> against
    /// <paramref name="rootPath"/>; <paramref name="escaped"/> reports a lexical escape.</summary>
    public static string Resolve(string rootPath, string relativePath, out bool escaped)
    {
        var combined = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        escaped = !IsWithin(rootPath, combined);
        return combined;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="rootPath"/> or inside it.</summary>
    public static bool IsWithin(string rootPath, string path) =>
        path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || string.Equals(path, rootPath, StringComparison.Ordinal);
}

internal static class OpenApiArtifact
{
    private static readonly Regex s_openApiVersion = new("^\\s*openapi\\s*:\\s*['\\\"]?(?<version>[^'\\\"\\s#]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex s_externalRef = new("^\\s*(?:\\\"?\\$ref\\\"?\\s*:)\\s*['\\\"]?(?!#)(?<reference>\\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static OpenApiMock Inspect(string root, DevelopmentBuilder.MockDeclaration declaration)
    {
        var appId = declaration.AppId;
        var relativePath = declaration.Path;
        if (relativePath is null)
        {
            throw new DevelopmentValidationException($"Mock for service '{appId}' does not declare an OpenAPI artifact.");
        }

        var rootPath = PathContainment.ResolveRoot(root);
        var artifactPath = PathContainment.Resolve(rootPath, relativePath, out var escaped);
        if (escaped)
        {
            throw new DevelopmentValidationException($"Mock artifact '{relativePath}' for service '{appId}' escapes the SystemHost directory.");
        }

        try
        {
            artifactPath = Path.GetFullPath(new FileInfo(artifactPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? artifactPath);
        }
        catch (IOException ex)
        {
            throw new DevelopmentValidationException($"Mock artifact '{relativePath}' for service '{appId}' cannot be resolved: {ex.Message}");
        }

        if (!PathContainment.IsWithin(rootPath, artifactPath))
        {
            throw new DevelopmentValidationException($"Mock artifact '{relativePath}' for service '{appId}' escapes the SystemHost directory through a symlink.");
        }

        string text;
        try
        {
            text = File.ReadAllText(artifactPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DevelopmentValidationException($"Mock artifact '{relativePath}' for service '{appId}' cannot be read: {ex.Message}");
        }

        var version = s_openApiVersion.Match(text).Groups["version"].Value;
        if (!Version.TryParse(version, out var parsedVersion) || parsedVersion.Major != 3 || parsedVersion.Minor != 0)
        {
            throw new DevelopmentValidationException($"Mock artifact '{relativePath}' for service '{appId}' must be an OpenAPI 3.0.x document.");
        }

        if (s_externalRef.IsMatch(text))
        {
            throw new DevelopmentValidationException($"Mock artifact '{relativePath}' for service '{appId}' contains an external $ref.");
        }

        try
        {
            var settings = new OpenApiReaderSettings();
            settings.AddYamlReader();
            var document = OpenApiDocument.LoadAsync(artifactPath, settings).GetAwaiter().GetResult().Document
                ?? throw new DevelopmentValidationException($"Mock artifact '{relativePath}' for service '{appId}' could not be parsed.");
            var info = document.Info;
            if (info is null || string.IsNullOrWhiteSpace(info.Title) || string.IsNullOrWhiteSpace(info.Version))
            {
                throw new DevelopmentValidationException($"Mock artifact '{relativePath}' for service '{appId}' must define non-empty info.title and info.version.");
            }

            return new OpenApiMock(appId, artifactPath, info.Title, info.Version);
        }
        catch (DevelopmentValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DevelopmentValidationException($"Mock artifact '{relativePath}' for service '{appId}' is not a valid OpenAPI document: {ex.Message}");
        }
    }
}
