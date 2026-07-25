using Intropy.Topology.Building;
using Intropy.Topology.Model;
using Intropy.Topology.Validation;

namespace Intropy.Topology;

/// <summary>
/// Entry point for declaring an integration system's topology. Add components, wire
/// them to topics and connectors, then call <see cref="Build"/> to materialize and
/// validate the immutable <see cref="SystemTopology"/>. Resources (topics, connectors)
/// materialize from usage — there is no <c>AddTopic</c>; shared platform services are
/// the exception, declared explicitly with <see cref="UsesService"/> because no edge
/// references them. Not thread-safe; a second <see cref="Build"/> after further
/// mutation reflects the mutations.
/// </summary>
public sealed class SystemBuilder
{
    private readonly List<Component> _components = [];
    private readonly List<ServiceRef> _services = [];

    /// <summary>The system's name (DNS-1123 label).</summary>
    public string SystemName { get; }

    private SystemBuilder(string systemName)
    {
        SystemName = systemName;
    }

    internal IReadOnlyList<Component> Components => _components;

    internal IReadOnlyList<ServiceRef> Services => _services;

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
    /// Declares a shared platform service the whole system depends on. The run backend
    /// starts it and starts every component only after it is ready. No connectivity is
    /// injected — starting the service and the startup ordering is the whole contract.
    /// </summary>
    /// <param name="service">The service the system uses.</param>
    public void UsesService(ServiceRef service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _services.Add(service);
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
