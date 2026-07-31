using Intropy.Topology.Generation;

/// <summary>Local OpenAPI-backed substitutes for order-flow platform services.</summary>
public sealed class OrderFlowDevelopment : IDevelopmentDefinition
{
    /// <inheritdoc />
    public void Define(DevelopmentBuilder development)
    {
        development.Mock(Services.Idempotency).FromOpenApi("mocks/idempotency-service.openapi.yaml");
        development.Mock(Services.BusinessIncidents).FromOpenApi("mocks/business-incident-service.openapi.yaml");
    }
}
