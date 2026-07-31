using Intropy.Topology;

/// <summary>Platform services used by the order-flow integrations.</summary>
public static class Services
{
    /// <summary>Stateless development substitute for the idempotency service.</summary>
    public static readonly ServiceRef Idempotency = ServiceRef.Define("idempotency-service");

    /// <summary>Stateless development substitute for the business-incident service.</summary>
    public static readonly ServiceRef BusinessIncidents = ServiceRef.Define("business-incident-service");
}
