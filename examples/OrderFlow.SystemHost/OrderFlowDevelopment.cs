using Intropy.Topology.Generation;

/// <summary>Local OpenAPI-backed substitutes and connector file resolutions for order-flow.</summary>
public sealed class OrderFlowDevelopment : IDevelopmentDefinition
{
    /// <inheritdoc />
    public void Define(DevelopmentBuilder development)
    {
        development.Mock(Services.Idempotency).FromOpenApi("mocks/idempotency-service.openapi.yaml");
        development.Mock(Services.BusinessIncidents).FromOpenApi("mocks/business-incident-service.openapi.yaml");
        development.Files(Connectors.OrderExtractorSource).RootPath("./test/order-extractor-source");
        development.Files(Connectors.OrderLoaderDestination).RootPath("./test/order-loader-destination");
    }
}
