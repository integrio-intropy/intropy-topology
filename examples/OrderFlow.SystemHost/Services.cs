using Intropy.Topology;

/// <summary>The shared platform services the system depends on. The run backend starts each one and starts every component only after it is ready; no connectivity is injected — components reach the service on its fixed port.</summary>
public static class Services
{
    /// <summary>The shared idempotency store (Redis on its default port).</summary>
    public static readonly ServiceRef Idempotency = ServiceRef.Define("idempotency", Service.Container("docker.io/library/redis:7", 6379));
}
