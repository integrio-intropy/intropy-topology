using Intropy.Topology.Building;
using Intropy.Topology.Model;
using Intropy.Topology.Validation;

namespace Intropy.Topology;

/// <summary>
/// Entry point for declaring an integration system's topology. Add components, wire
/// them to messages and ports, then call <see cref="Build"/> to materialize and
/// validate the immutable <see cref="SystemTopology"/>. Resources (messages, ports)
/// materialize from usage — there is no <c>AddMessage</c>. Not thread-safe;
/// a second <see cref="Build"/> after further mutation reflects the mutations.
/// </summary>
public sealed class SystemBuilder
{
    private readonly List<Component> _components = [];
    private OtlpBuilder? _otlp;

    /// <summary>The system's name (DNS-1123 label).</summary>
    public string SystemName { get; }

    private SystemBuilder(string systemName)
    {
        SystemName = systemName;
    }

    internal IReadOnlyList<Component> Components => _components;

    /// <summary>The OTLP settings declared for this system, or null when none.</summary>
    internal OtlpSettings? OtlpDeclaration => _otlp?.Settings;

    /// <summary>Creates a builder for a named system.</summary>
    /// <param name="systemName">The system's name (DNS-1123 label).</param>
    /// <exception cref="ArgumentException">The name is not a valid DNS-1123 label.</exception>
    public static SystemBuilder Create(string systemName) =>
        new(NameRules.RequireLabel(systemName, nameof(systemName)));

    /// <summary>Adds an extractor: pulls data out of an external system and publishes it.</summary>
    /// <param name="name">The component's name (DNS-1123 label).</param>
    public ExtractorBuilder AddExtractor(string name) =>
        new(Register(new ExtractorComponent(NameRules.RequireLabel(name, nameof(name)))));

    /// <summary>Adds a loader: consumes a topic and writes to an external system.</summary>
    /// <param name="name">The component's name (DNS-1123 label).</param>
    public LoaderBuilder AddLoader(string name) =>
        new(Register(new LoaderComponent(NameRules.RequireLabel(name, nameof(name)))));

    /// <summary>Adds a transactional integration.</summary>
    /// <param name="name">The component's name (DNS-1123 label).</param>
    public TransactionalIntegrationBuilder AddTransactionalIntegration(string name) =>
        new(Register(new TransactionalIntegrationComponent(NameRules.RequireLabel(name, nameof(name)))));

    /// <summary>
    /// Declares the OTLP endpoint the whole system (every component and its Dapr sidecar)
    /// exports telemetry to, in place of each runtime's default sink. A topology that
    /// declares nothing keeps its runtime defaults — the declaration only ever redirects.
    /// One system streams to one sink: a second call throws rather than compose or replace.
    /// </summary>
    /// <param name="endpoint">The OTLP base endpoint, an absolute HTTP(S) URL.</param>
    /// <exception cref="ArgumentException">The endpoint is not an absolute HTTP(S) URL.</exception>
    /// <exception cref="InvalidOperationException">OTLP export was already declared.</exception>
    /// <returns>The builder scoped to this declaration.</returns>
    public OtlpBuilder Otlp(string endpoint)
    {
        if (_otlp is not null)
        {
            throw new InvalidOperationException(
                $"OTLP export is already declared for system '{SystemName}'.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || !IsValidEndpointHost(uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"'{endpoint}' is not an absolute HTTP(S) URL.", nameof(endpoint));
        }

        // Validated with Uri, stored as declared: canonicalizing here would silently rewrite
        // the sink (a trailing slash, lower-cased scheme) on its way into every variable.
        _otlp = OtlpBuilder.ForEndpoint(endpoint);
        return _otlp;
    }

    private static bool IsValidEndpointHost(Uri uri)
    {
        var host = uri.Host;
        return host.Length > 0
            && !host.Any(char.IsWhiteSpace);
    }

    /// <summary>
    /// Materializes and validates the topology. Throws when any error-severity
    /// violation exists, carrying every diagnostic found.
    /// </summary>
    /// <exception cref="TopologyValidationException">The declared topology is invalid.</exception>
    public SystemTopology Build() =>
        TryBuild(out var topology, out var diagnostics)
            ? topology!
            : throw new TopologyValidationException(diagnostics);

    /// <summary>Non-throwing <see cref="Build"/>: returns false with the diagnostics when the topology is invalid.</summary>
    /// <param name="topology">The topology when valid; null otherwise.</param>
    /// <param name="diagnostics">All diagnostics found, including warnings.</param>
    public bool TryBuild(out SystemTopology? topology, out IReadOnlyList<TopologyDiagnostic> diagnostics)
    {
        var candidate = TopologyMaterializer.Materialize(this);
        diagnostics = TopologyRules.Run(this, candidate);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            topology = null;
            return false;
        }

        topology = candidate;
        return true;
    }

    /// <summary>Runs validation without throwing; returns all diagnostics including warnings.</summary>
    public IReadOnlyList<TopologyDiagnostic> Validate() =>
        TopologyRules.Run(this, TopologyMaterializer.Materialize(this));

    private T Register<T>(T component) where T : Component
    {
        _components.Add(component);
        return component;
    }
}

/// <summary>
/// Fluent builder for a system's OTLP export declaration, received from
/// <see cref="SystemBuilder.Otlp"/>. Configures the settings materialized into
/// <see cref="SystemTopology.Otlp"/>; the declaration is read once <c>Define</c> returns,
/// so chain members after the call rather than holding the builder. Not thread-safe.
/// </summary>
public sealed class OtlpBuilder
{
    private OtlpSettings _settings;

    private OtlpBuilder(string endpoint) =>
        _settings = new OtlpSettings { Endpoint = endpoint };

    internal static OtlpBuilder ForEndpoint(string endpoint) => new(endpoint);

    /// <summary>The current settings, read by the materializer after declaration completes.</summary>
    internal OtlpSettings Settings => _settings;

    /// <summary>
    /// Sets the OTLP wire protocol; gRPC unless declared otherwise.
    /// </summary>
    /// <param name="protocol">The wire protocol every export uses.</param>
    public OtlpBuilder WithProtocol(OtlpProtocol protocol)
    {
        _settings = _settings with { Protocol = protocol };
        return this;
    }

    /// <summary>
    /// Adds an HTTP header sent with every export request. Values pass through verbatim:
    /// an environment placeholder such as <c>${OTLP_API_KEY}</c> is resolved by the runtime
    /// that reads the variables, never by the topology declaration — declare secrets here
    /// only as placeholders. A repeated name replaces the previous value.
    /// </summary>
    /// <param name="name">The header name, valid as an HTTP header token.</param>
    /// <param name="value">The header value, or an environment placeholder for one.</param>
    /// <exception cref="ArgumentException">The name is not a valid HTTP header token, or the
    /// value is empty or contains control characters.</exception>
    public OtlpBuilder WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (name.Any(c => !IsTokenChar(c)))
        {
            throw new ArgumentException($"'{name}' is not a valid HTTP header name.", nameof(name));
        }

        if (value.Any(c => char.IsControl(c)))
        {
            throw new ArgumentException("Header values must not contain control characters.", nameof(value));
        }

        var headers = new Dictionary<string, string>(_settings.Headers, StringComparer.Ordinal)
        {
            [name] = value,
        };
        _settings = _settings with { Headers = headers };
        return this;
    }

    // RFC 9110 field-name token: the characters beyond the alphanumeric range that a header
    // name may carry. Anything else (notably separators and whitespace) formulates a broken
    // OTEL_EXPORTER_OTLP_HEADERS value downstream, so it is rejected at the call site.
    private static bool IsTokenChar(char c) =>
        char.IsAsciiLetterOrDigit(c)
        || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*'
            or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
