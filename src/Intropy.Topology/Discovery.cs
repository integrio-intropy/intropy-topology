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

        var definition = (ISystemDefinition)Activator.CreateInstance(candidates[0])!;
        var builder = SystemBuilder.Create(definition.SystemName);
        definition.Define(builder);
        return new DiscoveredSystem(builder.Build(), definition);
    }
}
