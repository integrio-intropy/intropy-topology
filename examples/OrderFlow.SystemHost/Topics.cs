using Intropy.Topology;
using Contracts;

/// <summary>The system's topics, each defined once and shared by every component that touches it.</summary>
public static class Topics
{
    /// <summary>Order messages on topic 'orders' (pubsub 'pubsub').</summary>
    public static readonly TopicRef<Order> Orders = TopicRef<Order>.Define("pubsub", "orders");
}
