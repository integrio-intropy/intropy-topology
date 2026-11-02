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

    /// <summary>Consumes a topic, decides each message against the state it keeps, and publishes
    /// new messages committed together with that state.</summary>
    Processor,
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

    /// <summary>Consults the external system mid-flow, request/response, without it driving or
    /// receiving the flow (<c>LooksUp</c>): enrichment, such as resolving what an event refers to.
    /// At the Dapr level an output-binding invocation (e.g. <c>get</c>), never a trigger.</summary>
    Lookup,
}
