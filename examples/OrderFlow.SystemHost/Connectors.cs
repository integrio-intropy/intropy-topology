using Intropy.Topology;

/// <summary>Defines the system-owned connectors used by component edges.</summary>
public static class Connectors
{
    /// <summary>Uses <c>./test/order-extractor-source</c> as the local file endpoint.</summary>
    public static readonly ConnectorRef OrderExtractorSource = ConnectorRef.Define("order-extractor-source", Transport.File("./test/order-extractor-source"));

    /// <summary>Uses <c>./test/order-loader-destination</c> as the local file endpoint.</summary>
    public static readonly ConnectorRef OrderLoaderDestination = ConnectorRef.Define("order-loader-destination", Transport.File("./test/order-loader-destination"));
}
