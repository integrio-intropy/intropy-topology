# Intropy Topology

> A typed, fluent DSL for declaring integration-system topologies in .NET.

## Overview

Intropy.Topology lets a SystemHost project declare a system's components (extractors, loaders, transactional integrations) and the edges between them (topics, connectors) in code. `Build()` validates the declaration and produces an immutable, serializable `SystemTopology` that the generation and Aspire backends consume.

This is the **minimal model** — exactly the surface the system tutorial and `intropy sys create` use. The full model (aggregators, APIs, named publish ports, sftp/blob/http/custom transports, dev mocking) is preserved on the `full-topology` branch ([ADR 0009](decisions/0009-minimal-model-for-system-tutorial.md)).

The topology models the edges *between* components — subscriptions, publishes, connectors — plus one activation attribute: an optional cron schedule (`WithSchedule`, extractors only; [ADR 0010](decisions/0010-schedule-as-activation-attribute.md)). Everything else about the workload shape lives in the component's own scaffold.

The library's signature move is **compile-time legality**: each `Add*` call returns a block-specific builder that exposes only the grammar legal for that block. Illegal topology is a compile error, not a validation diagnostic; only what types cannot check (completeness and cross-component conflicts) is validated at `Build()`.

## Installation

```bash
dotnet add package Intropy.Topology
```

## Quick example

```csharp
var s = SystemBuilder.Create("order-flow");

s.AddExtractor("order-extractor")
    .From(Connectors.OrderExtractorSource)
    .Publishes(Topics.Orders);

s.AddLoader("order-loader")
    .Subscribes(Topics.Orders)
    .To(Connectors.OrderLoaderDestination);

SystemTopology topology = s.Build();   // throws TopologyValidationException with all violations
```

Topics and connectors are declared as static fields in a scaffolded `Topics.cs` / `Connectors.cs`; resources materialize from usage — there is no `AddTopic`.

## Key features

- **Compile-time legality** — block builders make illegal topology uncompilable. [Learn more](concepts/components.md)
- **Typed refs** — `TopicRef<T>` carries an async event contract; `ConnectorRef` names the port an edge block reaches the outside world through. [Learn more](concepts/topics.md)
- **Transports select Dapr bindings** — every connector materializes as exactly one Dapr binding component; the file transport (`bindings.localstorage`) makes the folder the endpoint. [Learn more](concepts/connectors.md)
- **Collect-all validation** — `Build()` reports every violation at once with stable `ITP` diagnostic codes. [Learn more](concepts/validation.md)
- **Deterministic materialization** — the output model is immutable, sorted, and byte-stable across runs. [Learn more](concepts/materialization.md)

## Documentation

| Section | Description |
|---------|-------------|
| [Getting Started](getting-started.md) | Declare the complete order-flow topology from scratch |
| [Components](concepts/components.md) | Component kinds, block builders, and their legal edges |
| [Topics](concepts/topics.md) | Typed topic refs and the materialize-from-usage model |
| [Connectors](concepts/connectors.md) | Connectors, the file transport, and derived binding names |
| [Validation](concepts/validation.md) | The ITP diagnostic codes and the Build/TryBuild/Validate API |
| [Materialization](concepts/materialization.md) | How declarations fold into the immutable model |
| [Decisions](decisions/0001-staged-per-block-builders.md) | Design decision records |

## Requirements

- .NET 10 or later
- For the Aspire `run` backend: Docker and the Dapr CLI (`dapr init`)
