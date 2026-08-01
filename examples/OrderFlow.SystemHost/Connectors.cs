using Intropy.Topology;

/// <summary>Defines the system-owned connectors used by component edges.</summary>
public static class Connectors
{
    /// <summary>Deployed as SFTP; locally resolved to <c>./test/order-extractor-source</c>.</summary>
    public static readonly ConnectorRef OrderExtractorSource = ConnectorRef.Define("order-extractor-source", Transport.Sftp());

    /// <summary>Deployed as SFTP; locally resolved to <c>./test/order-loader-destination</c>.</summary>
    public static readonly ConnectorRef OrderLoaderDestination = ConnectorRef.Define("order-loader-destination", Transport.Sftp());
}
