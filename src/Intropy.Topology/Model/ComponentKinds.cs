namespace Intropy.Topology.Model;

/// <summary>
/// The one lifetime each component kind implies: whether its legitimate host runs it to
/// completion (one sweep, then exit) or keeps it resident. Derived from kind rather than
/// modeled — the topology records edges, not workload shape — and shared so that local
/// tooling (development-manifest validation, the Aspire host) agrees on the derivation.
/// If a kind ever gains a lifetime its edges do not imply, this must become an explicit
/// model fact.
/// </summary>
internal static class ComponentKinds
{
    /// <summary>Whether a component of <paramref name="kind"/> runs to completion.</summary>
    public static bool IsRunToCompletion(this ComponentKind kind) =>
        kind is ComponentKind.Extractor or ComponentKind.TransactionalIntegration;
}
