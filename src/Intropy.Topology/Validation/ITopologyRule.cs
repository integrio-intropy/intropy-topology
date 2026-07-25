using Intropy.Topology.Model;

namespace Intropy.Topology.Validation;

/// <summary>Everything a rule can inspect: the raw builder declarations and the materialized model.</summary>
internal sealed record ValidationContext(SystemBuilder Builder, SystemTopology Topology);

/// <summary>One structural/semantic validation rule, evaluated at Build time.</summary>
internal interface ITopologyRule
{
    IEnumerable<TopologyDiagnostic> Evaluate(ValidationContext context);
}
