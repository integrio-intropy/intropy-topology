using System.Reflection;
using System.Text.RegularExpressions;
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

/// <summary>Records OpenAPI-backed local service mocks. Not thread-safe.</summary>
public sealed class DevelopmentBuilder
{
    private readonly SystemTopology _topology;
    private readonly string _root;
    private readonly List<MockDeclaration> _mocks = [];
    private readonly List<FileDeclaration> _files = [];

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

    /// <summary>Declares the local file resolution for a connector used by this topology.</summary>
    /// <param name="connector">The connector to resolve locally.</param>
    public FileBuilder Files(ConnectorRef connector)
    {
        ArgumentNullException.ThrowIfNull(connector);
        if (!_topology.Connectors.Any(candidate => candidate.Name == connector.Name))
        {
            throw new DevelopmentValidationException($"Connector '{connector.Name}' is not used by the system topology.");
        }

        if (_files.Any(candidate => candidate.ConnectorName == connector.Name))
        {
            throw new DevelopmentValidationException($"Connector '{connector.Name}' has more than one file resolution.");
        }

        var declaration = new FileDeclaration(connector.Name);
        _files.Add(declaration);
        return new FileBuilder(declaration);
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

        var unresolved = _topology.Connectors
            .Where(connector => !_files.Any(file => file.ConnectorName == connector.Name))
            .Select(connector => connector.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unresolved.Length > 0)
        {
            throw new DevelopmentValidationException(
                $"Connectors '{string.Join("', '", unresolved)}' have no local file resolution.");
        }

        var files = _files.Select(file => FileRootPath.Inspect(_root, file)).ToArray();
        return new DevelopmentManifest(
            mocks.OrderBy(mock => mock.AppId, StringComparer.Ordinal).ToArray(),
            files.OrderBy(file => file.ConnectorName, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Configures the OpenAPI artifact for one service mock.</summary>
    public sealed class MockBuilder
    {
        private readonly MockDeclaration _declaration;
        internal MockBuilder(MockDeclaration declaration) => _declaration = declaration;

        /// <summary>Uses a self-contained OpenAPI 3.0.x document relative to the SystemHost directory.</summary>
        /// <param name="path">The relative contract path.</param>
        public void FromOpenApi(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (_declaration.Path is not null)
            {
                throw new DevelopmentValidationException($"Service '{_declaration.AppId}' has more than one OpenAPI artifact.");
            }

            _declaration.Path = path;
        }
    }

    /// <summary>Configures the local root path for one connector file resolution.</summary>
    public sealed class FileBuilder
    {
        private readonly FileDeclaration _declaration;
        internal FileBuilder(FileDeclaration declaration) => _declaration = declaration;

        /// <summary>Reads and writes files in a folder relative to the SystemHost directory.</summary>
        /// <param name="path">The relative folder path.</param>
        public void RootPath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (_declaration.Path is not null)
            {
                throw new DevelopmentValidationException($"Connector '{_declaration.ConnectorName}' has more than one root path.");
            }

            _declaration.Path = path;
        }
    }

    internal sealed class MockDeclaration(string appId)
    {
        public string AppId { get; } = appId;
        public string? Path { get; set; }
    }

    internal sealed class FileDeclaration(string connectorName)
    {
        public string ConnectorName { get; } = connectorName;
        public string? Path { get; set; }
    }
}

/// <summary>Validated development substitutions consumed by generation and local runtime backends.</summary>
public sealed record DevelopmentManifest(IReadOnlyList<OpenApiMock> Mocks, IReadOnlyList<ConnectorFileResolution> Files);

/// <summary>One connector's validated local file resolution.</summary>
public sealed record ConnectorFileResolution(string ConnectorName, string RootPath);

/// <summary>One OpenAPI-backed platform-service mock with a verified Microcks identity.</summary>
public sealed record OpenApiMock(string AppId, string ArtifactPath, string Title, string Version)
{
    /// <summary>The local Microcks REST base URI, with title and version escaped as individual path segments.</summary>
    public Uri BaseUri => new($"http://localhost:8585/rest/{Uri.EscapeDataString(Title)}/{Uri.EscapeDataString(Version)}", UriKind.Absolute);
}

/// <summary>Thrown when a development definition or its artifacts are invalid.</summary>
public sealed class DevelopmentValidationException : InvalidOperationException
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
        var candidates = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IDevelopmentDefinition).IsAssignableFrom(type))
            .ToArray();
        if (candidates.Length > 1)
        {
            throw new DevelopmentValidationException(
                $"Multiple {nameof(IDevelopmentDefinition)} types were found: {string.Join(", ", candidates.Select(type => type.FullName))}. Exactly zero or one is required.");
        }

        if (candidates.Length == 0)
        {
            return new DevelopmentManifest([], []);
        }

        var builder = new DevelopmentBuilder(topology, root);
        ((IDevelopmentDefinition)Activator.CreateInstance(candidates[0])!).Define(builder);
        return builder.Build();
    }
}

internal static class FileRootPath
{
    public static ConnectorFileResolution Inspect(string root, DevelopmentBuilder.FileDeclaration declaration)
    {
        var connectorName = declaration.ConnectorName;
        var relativePath = declaration.Path;
        if (relativePath is null)
        {
            throw new DevelopmentValidationException($"File resolution for connector '{connectorName}' does not declare a root path.");
        }

        var rootPath = ResolveDirectory(root, $"SystemHost directory '{root}' cannot be resolved");
        var folderPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (Directory.Exists(folderPath))
        {
            folderPath = ResolveDirectory(folderPath, $"File resolution '{relativePath}' for connector '{connectorName}' cannot be resolved");
        }

        if (!folderPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !string.Equals(folderPath, rootPath, StringComparison.Ordinal))
        {
            throw new DevelopmentValidationException($"File resolution '{relativePath}' for connector '{connectorName}' escapes the SystemHost directory.");
        }

        return new ConnectorFileResolution(connectorName, folderPath);
    }

    private static string ResolveDirectory(string path, string error)
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

        var rootPath = Path.GetFullPath(root);
        try
        {
            rootPath = Path.GetFullPath(new DirectoryInfo(rootPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? rootPath);
        }
        catch (IOException ex)
        {
            throw new DevelopmentValidationException($"SystemHost directory '{root}' cannot be resolved: {ex.Message}", ex);
        }

        var artifactPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!artifactPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !string.Equals(artifactPath, rootPath, StringComparison.Ordinal))
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

        if (!artifactPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
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
