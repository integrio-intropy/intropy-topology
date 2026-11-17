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

    /// <summary>Serves an HTTP endpoint callers push items to, and publishes each one.</summary>
    Gateway,

    /// <summary>Runs scheduled work to completion that no other kind describes.</summary>
    Job,
}

/// <summary>What happens to the messages on a subscribed channel that the subscription does not
/// handle.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<UnhandledMessages>))]
public enum UnhandledMessages
{
    /// <summary>Left for redelivery, so the broker dead-letters them (the default): a message
    /// nobody expected on the channel is visible and replayable.</summary>
    DeadLetter,

    /// <summary>Acknowledged and dropped: the channel carries messages this component deliberately
    /// ignores.</summary>
    Ignore,
}

/// <summary>The direction a component uses a port in.</summary>
public enum PortDirection
{
    /// <summary>Reads from the external system (<c>From</c> / port trigger).</summary>
    In,

    /// <summary>Writes to the external system (<c>To</c>).</summary>
    Out,
}

/// <summary>How a component reaches a port.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<PortKind>))]
public enum PortKind
{
    /// <summary>Through a Dapr binding named after the port (the default). The binding's type,
    /// address and credentials are deployment configuration.</summary>
    Binding,

    /// <summary>Reached by the component itself, through a client or adapter of its own (such as a
    /// database or an HTTP API), with settings the deployment supplies. There is no Dapr binding. A
    /// component that reads the port keeps a cursor between runs in a state store the topology
    /// mints; a component that writes to it needs only the settings.</summary>
    External,
}

/// <summary>What a system store keeps.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<StoreRole>))]
public enum StoreRole
{
    /// <summary>Payloads too large to travel inside a message, kept while a pointer travels
    /// instead (the claim check).</summary>
    Claim,

    /// <summary>Copies of the payloads components received, kept so an incident or a trace can
    /// show what arrived.</summary>
    Archive,
}
