using Intropy.Topology.Model;

namespace Intropy.Topology.Aspire;

/// <summary>
/// The subscriber-first startup ordering derived from a topology: which components each
/// publisher must wait for. Nobody declares this — it follows from who publishes and who
/// subscribes each topic. Pure functions over the model, unit-testable without DCP.
/// </summary>
internal static class StartupOrdering
{
    /// <summary>
    /// Every subscriber of every topic a component publishes. Self-loops (a component
    /// consuming its own topic) are not orderable edges and are excluded.
    /// </summary>
    public static Dictionary<string, HashSet<string>> SubscriberPrecedence(SystemTopology topology)
    {
        var precedence = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var topic in topology.Topics)
        {
            foreach (var publisher in topic.Publishers)
            {
                foreach (var subscriber in topic.Subscribers)
                {
                    if (string.Equals(publisher, subscriber, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!precedence.TryGetValue(publisher, out var subscribers))
                    {
                        precedence[publisher] = subscribers = new HashSet<string>(StringComparer.Ordinal);
                    }

                    subscribers.Add(subscriber);
                }
            }
        }

        return precedence;
    }

    /// <summary>
    /// Whether the precedence graph has a cycle. Publish/subscribe cycles cannot be
    /// ordered — the caller skips ordering entirely rather than deadlock.
    /// </summary>
    public static bool HasCycle(Dictionary<string, HashSet<string>> edges)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 = visiting, 2 = done

        bool Visit(string node)
        {
            if (state.TryGetValue(node, out var seen))
            {
                return seen == 1;
            }

            state[node] = 1;
            if (edges.TryGetValue(node, out var next) && next.Any(Visit))
            {
                return true;
            }

            state[node] = 2;
            return false;
        }

        return edges.Keys.Any(Visit);
    }
}
