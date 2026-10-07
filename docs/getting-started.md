# Getting Started

This guide walks you through declaring the order-flow topology — an extractor and a loader sharing one topic. By the end, you'll have a runnable SystemHost that validates the declaration, prints the materialized model, and generates Dapr components. The finished result lives in [`examples/OrderFlow.SystemHost`](../examples/OrderFlow.SystemHost/), whose declaration files mirror `intropy sys create` output verbatim.

## Prerequisites

- .NET 10 SDK
- A .NET console project (the SystemHost)

## Install the package

```bash
dotnet add package Intropy.Topology
```

## Declare your messages

Messages are declared as static fields — in a `Messages.cs` for system-internal messages, or in a shared contracts package for cross-system messages. A `MessageRef<T>` names the message's logical identity, its channel (pubsub + topic — the topic name defaults to the message name), and the event contract type `T` carried on it:

```csharp
public static class Messages
{
    public static readonly MessageRef<Order> Orders = MessageRef<Order>.Define("orders");
}
```

There is no `AddTopic` on the builder — publishing or subscribing to a message is what brings its channel (topic and pubsub) into the model.

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

## Declare platform services

A service is a Dapr app ID that components invoke. The topology records the dependency and its consumers; the service implementation is supplied by the platform or a local development substitute:

```csharp
public static class Services
{
    public static readonly ServiceRef Idempotency = ServiceRef.Define("idempotency-service");
}
```

## Declare component names

Component names are strings passed to the `Add*` calls, and the development definition references components by name again. A `Components.cs` holding the names as constants — alongside the other ref files — keeps the compiler between the two definitions, so a rename refactors both:

```csharp
public static class Components
{
    public const string OrderExtractor = "order-extractor";
    public const string OrderLoader = "order-loader";
}
```

## Declare the system

A system is a class implementing `ISystemDefinition`. Each `Add*` call returns the block's builder directly, exposing only the edges legal for that block:

```csharp
public sealed class OrderFlowSystem : ISystemDefinition
{
    public string SystemName => "order-flow";

    public void Define(SystemBuilder builder)
    {
        // Extractor: edge block, pulls data out through a port and publishes it.
        builder.AddExtractor(Components.OrderExtractor)
            .From(Ports.OrderExtractorSource)
            .Publishes(Messages.Orders)
            .Calls(Services.Idempotency);

        // Loader: edge block, subscribes to one channel — here handling one message on it —
        // and writes through a port. More of the channel: .Subscribes(a, configure: sub => sub.AlsoHandles(b)).
        builder.AddLoader(Components.OrderLoader)
            .Subscribes(Messages.Orders)
            .To(Ports.OrderLoaderDestination)
            .Calls(Services.Idempotency);
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

## Declare local development substitutions

When the SystemHost uses `Intropy.Topology.Generation`, an optional `IDevelopmentDefinition` resolves topology facts for local runs and generation. It cannot introduce new ports or services; every reference must already be used by the topology:

```csharp
public sealed class OrderFlowDevelopment : IDevelopmentDefinition
{
    public void Define(DevelopmentBuilder development)
    {
        development.Mock(Services.Idempotency)
            .FromOpenApi("mocks/idempotency-service.openapi.yaml");

        development.Files(Ports.OrderExtractorSource)
            .RootPath("./test/order-extractor-source");

        development.Files(Ports.OrderLoaderDestination)
            .RootPath("./test/order-loader-destination");

        development.Rerun(Components.OrderExtractor)
            .AfterEachRun(TimeSpan.FromSeconds(30));
    }
}
```

`Rerun` names a run-to-completion component — the shared `Components` constant keeps the name compiling against the system declaration. `AfterEachRun` waits after a run *completes* before starting the next; it is local host mechanism only, never written into generated artifacts — the deployed schedule lives in deployment configuration.

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

- [Model and DSL Reference](concepts/model.md) — the full declaration grammar and `SystemTopology` shape
- [Components](concepts/components.md) — the component kinds and their block builders
- [Messages](concepts/messages.md) — the asynchronous edge between components
- [Ports](concepts/ports.md) — port-named bindings and environment-owned deployment
- [Validation](concepts/validation.md) — the validation rules
