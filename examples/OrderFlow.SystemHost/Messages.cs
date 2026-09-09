using Intropy.Topology;
using Contracts;

/// <summary>The system's messages, each defined once and shared by every component that touches it.</summary>
public static class Messages
{
    /// <summary>Order messages (pubsub 'pubsub'); the topic name defaults to the message name.</summary>
    public static readonly MessageRef<Order> Orders = MessageRef<Order>.Define("orders", "pubsub");
}
