using Intropy.Topology.Model;

namespace Intropy.Topology.Validation;

/// <summary>
/// A rule over the materialized <see cref="SystemTopology"/> — the model view. Model rules
/// must not touch builder state: the model is the complete picture for anything the
/// materializer preserves (structure, usage, collisions, one-sidedness).
/// </summary>
internal interface IModelRule
{
    IEnumerable<TopologyDiagnostic> Evaluate(SystemTopology topology);
}

/// <summary>
/// A rule over the raw builder declarations — the declaration view. Declaration rules
/// exist only for detail the materializer erases or dedups: service duplicates
/// (deduplicated in the model), connector directions per call, and the topic contract
/// <see cref="Type"/> (the model carries only its name). Rules whose needs the model
/// already meets must be model rules instead.
/// </summary>
internal interface IDeclarationRule
{
    IEnumerable<TopologyDiagnostic> Evaluate(SystemBuilder builder);
}
