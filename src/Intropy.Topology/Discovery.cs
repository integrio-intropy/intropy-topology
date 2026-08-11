using System.Reflection;
using Intropy.Topology.Model;

namespace Intropy.Topology;

/// <summary>
/// A discoverable system declaration. A system is a type implementing this interface; it names the
/// system and declares its topology into the supplied <see cref="SystemBuilder"/>.
/// <see cref="SystemDiscovery.Discover"/> finds, instantiates, and builds it.
/// </summary>
public interface ISystemDefinition
{
    /// <summary>The system's name (DNS-1123 label).</summary>
    string SystemName { get; }

    /// <summary>Declares the system's components and edges into <paramref name="builder"/>.</summary>
    /// <param name="builder">A builder created for <see cref="SystemName"/>.</param>
    void Define(SystemBuilder builder);
}

/// <summary>The result of discovering a system: its validated topology and the definition instance.</summary>
/// <param name="Topology">The materialized, validated topology.</param>
/// <param name="Definition">The definition instance (carries optional seams such as dev profiles).</param>
public sealed record DiscoveredSystem(SystemTopology Topology, ISystemDefinition Definition);

/// <summary>Discovers and builds the single <see cref="ISystemDefinition"/> declared in an assembly.</summary>
public static class SystemDiscovery
{
    /// <summary>
    /// Finds the one concrete <see cref="ISystemDefinition"/> in <paramref name="assembly"/>,
    /// instantiates it via its parameterless constructor, declares its topology, and validates it.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The validated topology and the definition instance.</returns>
    /// <exception cref="InvalidOperationException">No definition, or more than one, was found.</exception>
    /// <exception cref="TopologyValidationException">The declared topology is invalid.</exception>
    public static DiscoveredSystem Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return DiscoverFrom(assembly.GetName().Name ?? assembly.ToString(), assembly.GetTypes());
    }

    internal static DiscoveredSystem DiscoverFrom(string source, IReadOnlyList<Type> types)
    {
        var (definition, builder) = Declare(source, types);
        return new DiscoveredSystem(builder.Build(), definition);
    }

    /// <summary>
    /// Discovery for read-only inspection (the <c>graph</c> verb): like <see cref="Discover"/>,
    /// but warning-severity diagnostics do not fail — the materialized topology is returned
    /// alongside them so a partially built system can still be inspected. Error-severity
    /// violations still throw.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="diagnostics">All diagnostics found, including the tolerated warnings.</param>
    /// <returns>The materialized topology and the definition instance.</returns>
    /// <exception cref="InvalidOperationException">No definition, or more than one, was found.</exception>
    /// <exception cref="TopologyValidationException">The declared topology has error-severity violations.</exception>
    public static DiscoveredSystem DiscoverTolerant(Assembly assembly, out IReadOnlyList<TopologyDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var (definition, builder) = Declare(assembly.GetName().Name ?? assembly.ToString(), assembly.GetTypes());
        if (!builder.TryBuild(out var topology, out diagnostics))
        {
            throw new TopologyValidationException(diagnostics);
        }

        return new DiscoveredSystem(topology!, definition);
    }

    private static (ISystemDefinition Definition, SystemBuilder Builder) Declare(string source, IReadOnlyList<Type> types)
    {
        var result = SingleImplementation.Find<ISystemDefinition>(types, requireOne: true);
        if (result.Error is not null)
        {
            throw new InvalidOperationException($"{result.Error} in '{source}'.");
        }

        var definition = result.Instance!;
        var builder = SystemBuilder.Create(definition.SystemName);
        definition.Define(builder);
        return (definition, builder);
    }
}

/// <summary>
/// Scans a type list for the concrete implementations of an interface and reports the
/// arity outcome. The scan is shared by system and development discovery; each caller
/// maps a failure to its own exception type, so the helper never throws for arity.
/// Instantiation itself may throw (no parameterless constructor, constructor failure)
/// and does — that is a programming error, not an arity problem.
/// </summary>
internal static class SingleImplementation
{
    /// <summary>Finds zero-or-one or exactly one concrete <typeparamref name="T"/> in <paramref name="types"/>.</summary>
    /// <typeparam name="T">The interface or base type implementations must assign to.</typeparam>
    /// <param name="types">The types to scan.</param>
    /// <param name="requireOne">Whether zero candidates is a failure (exactly one
    /// required) or a legal empty result (zero or one allowed).</param>
    public static Result<T> Find<T>(IReadOnlyList<Type> types, bool requireOne) where T : class
    {
        var candidates = types
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(T).IsAssignableFrom(t))
            .ToList();

        if (candidates.Count == 0)
        {
            return requireOne
                ? new Result<T>(null, $"No {typeof(T).Name} was found")
                : new Result<T>(null, null);
        }

        if (candidates.Count > 1)
        {
            var names = string.Join(", ", candidates.Select(t => t.FullName));
            var arity = requireOne ? "Exactly one is required." : "Exactly zero or one is required.";
            return new Result<T>(null, $"Multiple {typeof(T).Name} types were found: {names}. {arity}");
        }

        return new Result<T>((T)Activator.CreateInstance(candidates[0])!, null);
    }

    /// <summary>The scan outcome: the instantiated single implementation, or an error
    /// describing the arity failure. Both are null when zero candidates are legal.</summary>
    internal sealed record Result<T>(T? Instance, string? Error) where T : class;
}
