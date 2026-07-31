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

## Declare your connectors

A connector is the named port an edge block reaches the outside world through. Its transport selects the Dapr binding type it materializes as; the Dapr component name is always derived from the connector's name (`binding.order-extractor-source`), never declared:

```csharp
public static class Connectors
{
    public static readonly ConnectorRef OrderExtractorSource =
        ConnectorRef.Define("order-extractor-source", Transport.File("./test/order-extractor-source"));

    public static readonly ConnectorRef OrderLoaderDestination =
        ConnectorRef.Define("order-loader-destination", Transport.File("./test/order-loader-destination"));
}
```

`Transport.File(rootPath)` (`bindings.localstorage`) makes the folder the endpoint, so the system runs with zero external configuration. Connectors are system-owned and never shared across systems. Direction is not part of the identity — it follows from usage (`From` reads, `To` writes).

## Declare the system

A system is a class implementing `ISystemDefinition`. Each `Add*` call returns the block's builder directly, exposing only the edges legal for that block:

```csharp
public sealed class OrderFlowSystem : ISystemDefinition
{
    public string SystemName => "order-flow";

    public void Define(SystemBuilder builder)
    {
        // Extractor: edge block, pulls data out through a connector and publishes it.
        builder.AddExtractor("order-extractor")
            .From(Connectors.OrderExtractorSource)
            .Publishes(Topics.Orders);

        // Loader: edge block, subscribes to exactly one topic and writes through a connector.
        builder.AddLoader("order-loader")
            .Subscribes(Topics.Orders)
            .To(Connectors.OrderLoaderDestination);
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

`generate` emits `components/<pubsub>.yaml` (Redis-backed pub/sub), one `components/binding.<connector>.yaml` per connector (root path absolutized), and `config/<component>.intropy.json` per component.

## Next steps

- [Components](concepts/components.md) — the component kinds and their block builders
- [Topics](concepts/topics.md) — the asynchronous edge between components
- [Connectors](concepts/connectors.md) — the file transport and derived binding names
- [Validation](concepts/validation.md) — the validation rules
