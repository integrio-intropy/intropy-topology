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
        var definition = FindSingleDefinition(source, types);
        var builder = SystemBuilder.Create(definition.SystemName);
        definition.Define(builder);
        return (definition, builder);
    }

    private static ISystemDefinition FindSingleDefinition(string source, IReadOnlyList<Type> types)
    {
        var candidates = types
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ISystemDefinition).IsAssignableFrom(t))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No {nameof(ISystemDefinition)} was found in '{source}'.");
        }

        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple {nameof(ISystemDefinition)} types were found in '{source}': " +
                $"{string.Join(", ", candidates.Select(t => t.FullName))}. Exactly one is required.");
        }

        return (ISystemDefinition)Activator.CreateInstance(candidates[0])!;
    }
}
