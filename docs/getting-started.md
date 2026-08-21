# Getting Started

This guide walks you through declaring the order-flow topology — an extractor and a loader sharing one topic. By the end, you'll have a runnable SystemHost that validates the declaration, prints the materialized model, and generates Dapr components. The finished result lives in [`examples/OrderFlow.SystemHost`](../examples/OrderFlow.SystemHost/), whose declaration files mirror `intropy sys create` output verbatim.

## Prerequisites

- .NET 10 SDK
- A .NET console project (the SystemHost)

## Install the package

```bash
dotnet add package Intropy.Topology
```

## Declare your topics

Topics are declared as static fields — in a `Topics.cs` for system-internal topics, or in a shared contracts package for cross-system topics. `TopicRef<T>` carries the event contract type carried on the topic:

```csharp
public static class Topics
{
    public static readonly TopicRef<Order> Orders = TopicRef<Order>.Define("pubsub", "orders");
}
```

There is no `AddTopic` on the builder — publishing to or subscribing to a topic is what brings it (and its pubsub) into the model.

## Declare your ports

A port is the named port an edge block reaches the outside world through. The Dapr binding component takes the port's name unchanged (`order-extractor-source`), never declared separately:

```csharp
public static class Ports
{
    public static readonly PortRef OrderExtractorSource =
        PortRef.Define("order-extractor-source");

    public static readonly PortRef OrderLoaderDestination =
        PortRef.Define("order-loader-destination");
}
```

The name is the port's whole identity; the deployed binding's type and credentials are environment-owned deployment configuration. Locally, the development definition resolves every port to a folder on the host, so the system runs with zero external configuration. Ports are system-owned and never shared across systems. Direction is not part of the identity — it follows from usage (`From` reads, `To` writes).

## Declare the system

A system is a class implementing `ISystemDefinition`. Each `Add*` call returns the block's builder directly, exposing only the edges legal for that block:

```csharp
public sealed class OrderFlowSystem : ISystemDefinition
{
    public string SystemName => "order-flow";

    public void Define(SystemBuilder builder)
    {
        // Extractor: edge block, pulls data out through a port and publishes it.
        builder.AddExtractor("order-extractor")
            .From(Ports.OrderExtractorSource)
            .Publishes(Topics.Orders);

        // Loader: edge block, subscribes to exactly one topic and writes through a port.
        builder.AddLoader("order-loader")
            .Subscribes(Topics.Orders)
            .To(Ports.OrderLoaderDestination);
    }
}
```

A method that would be illegal for the block type does not exist on its builder — `AddLoader(...).Publishes(topic)` is a compile error, because loaders publish nothing, and `AddExtractor(...).Subscribes(topic)` does not compile, because extractors don't subscribe.

## Build and inspect

`Build()` materializes and validates the topology. It throws a `TopologyValidationException` carrying every diagnostic at once when the declaration is invalid:

```csharp
SystemTopology topology;
try
{
    topology = builder.Build();
}
catch (TopologyValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
```

Use `TryBuild(out var topology, out var diagnostics)` to inspect diagnostics (including warnings) without throwing, or `Validate()` to run the rules without building.

## Wire up the entry point

The SystemHost's `Program.cs` routes one entry point to two backends of the same discovered topology:

```csharp
var assembly = Assembly.GetExecutingAssembly();

return args is ["run", ..] or []
    ? await IntropyAspire.RunAsync(assembly, args)    // → .NET Aspire + DCP (local F5)
    : await IntropyGenerate.RunAsync(assembly, args); // → generate | check | graph
```

```bash
dotnet run                       # Aspire dashboard (needs Docker + `dapr init`)
dotnet run -- check              # validate the topology
dotnet run -- graph              # print the SystemTopology JSON
dotnet run -- generate ./out     # write Dapr YAML + per-component config
```

`generate` emits `components/<pubsub>.yaml` (Redis-backed pub/sub), one `components/<port>.yaml` binding per port (root path relative to the SystemHost directory), and `config/<component>.intropy.json` per component. A transactional integration additionally gets `components/internal-<component>.yaml` — the Redis pub/sub backing its internal receive-to-send hop, scoped to itself; the runner picks the names up from its `.intropy.json`, so no hand-maintained Dapr component is needed.

## Next steps

- [Components](concepts/components.md) — the component kinds and their block builders
- [Topics](concepts/topics.md) — the asynchronous edge between components
- [Ports](concepts/ports.md) — port-named bindings and environment-owned deployment
- [Validation](concepts/validation.md) — the validation rules
