namespace Intropy.Topology.Model;

/// <summary>The kind a component was declared as.</summary>
public enum ComponentKind
{
    /// <summary>Pulls data out of an external system and publishes it.</summary>
    Extractor,

    /// <summary>Consumes a topic and writes to an external system.</summary>
    Loader,

    /// <summary>End-to-end transactional integration.</summary>
    TransactionalIntegration,
}

/// <summary>The direction a component uses a connector in.</summary>
public enum ConnectorDirection
{
    /// <summary>Reads from the external system (<c>From</c> / connector trigger).</summary>
    In,

    /// <summary>Writes to the external system (<c>To</c>).</summary>
    Out,
}
