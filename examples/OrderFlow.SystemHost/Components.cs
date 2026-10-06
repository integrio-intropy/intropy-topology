/// <summary>The system's component names, shared by the system definition and the development definition so a rename refactors both.</summary>
public static class Components
{
    /// <summary>Run-to-completion extractor: reads <see cref="Ports.OrderExtractorSource"/> and publishes <see cref="Messages.Orders"/>.</summary>
    public const string OrderExtractor = "order-extractor";

    /// <summary>Resident loader: subscribes to <see cref="Messages.Orders"/> and writes <see cref="Ports.OrderLoaderDestination"/>.</summary>
    public const string OrderLoader = "order-loader";
}
