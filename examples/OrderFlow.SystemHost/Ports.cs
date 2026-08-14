using Intropy.Topology;

/// <summary>Defines the system-owned ports used by component edges.</summary>
public static class Ports
{
    /// <summary>Deployed binding type is environment-owned; locally resolved to <c>./test/order-extractor-source</c>.</summary>
    public static readonly PortRef OrderExtractorSource = PortRef.Define("order-extractor-source");

    /// <summary>Deployed binding type is environment-owned; locally resolved to <c>./test/order-loader-destination</c>.</summary>
    public static readonly PortRef OrderLoaderDestination = PortRef.Define("order-loader-destination");
}
