using Intropy.Topology;

/// <summary>The system's connectors: the named ports its edge blocks reach the outside world through. Each defaults to a local file folder under test/ so the system runs with zero external configuration.</summary>
public static class Connectors
{
    /// <summary>Local file connector 'order-extractor-source' (folder ./test/order-extractor-source). Point it at a real external system and transport when known.</summary>
    public static readonly ConnectorRef OrderExtractorSource = ConnectorRef.Define("order-extractor-source", Transport.File("./test/order-extractor-source"));

    /// <summary>Local file connector 'order-loader-destination' (folder ./test/order-loader-destination). Point it at a real external system and transport when known.</summary>
    public static readonly ConnectorRef OrderLoaderDestination = ConnectorRef.Define("order-loader-destination", Transport.File("./test/order-loader-destination"));
}
