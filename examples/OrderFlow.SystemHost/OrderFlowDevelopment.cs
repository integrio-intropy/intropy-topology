using Intropy.Topology.Generation;

/// <summary>Local OpenAPI-backed substitutes and port file resolutions for order-flow.</summary>
public sealed class OrderFlowDevelopment : IDevelopmentDefinition
{
    /// <inheritdoc />
    public void Define(DevelopmentBuilder development)
    {
        development.Mock(Services.Idempotency).FromOpenApi("mocks/idempotency-service.openapi.yaml");
        development.Mock(Services.BusinessIncidents).FromOpenApi("mocks/business-incident-service.openapi.yaml");
        development.Files(Ports.OrderExtractorSource).RootPath("./test/order-extractor-source");
        development.Files(Ports.OrderLoaderDestination).RootPath("./test/order-loader-destination");
        // Local-only re-run cadence: how long the host waits after a sweep completes before
        // starting the next one. It never travels — the deployed schedule lives in deployment configuration.
        development.Rerun(Components.OrderExtractor).AfterEachRun(TimeSpan.FromSeconds(30));
    }
}
