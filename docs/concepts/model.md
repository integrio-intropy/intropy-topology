# Intropy Topology Model and DSL Reference

> A self-contained reference for declaring an integration-system topology with `Intropy.Topology` and understanding the immutable `SystemTopology` model produced by the DSL.

This is the C# implementation reference. The [System Model](system-model.md) defines the concepts; the [Topology Declaration Language](declaration-language.md) defines their relationships and rules without prescribing syntax or a runtime.

## What this model describes

An Intropy topology is a typed declaration of an integration system:

- which integration components exist;
- which messages move between components;
- which outside-world ports components read from or write to;
- which platform services components invoke through Dapr service invocation;
- where the whole system exports telemetry, when an OTLP sink is declared.

The topology is source data for generators and local runtime backends. A SystemHost project declares the topology in C#; `Build()` validates it and produces a serializable `SystemTopology` record.

The model deliberately describes **edges, not triggers**. It owns stable names and contracts that every backend must agree on. It does not own deployment details such as cron schedules, Kubernetes replicas, resource limits, binding `spec.type`, hostnames, credentials, or overlay-specific configuration.

## Packages and namespaces

The core DSL lives in the `Intropy.Topology` package:

```bash
dotnet add package Intropy.Topology
```

```csharp
using Intropy.Topology;
using Intropy.Topology.Model;
```

Optional local development declarations live in `Intropy.Topology.Generation`:

```bash
dotnet add package Intropy.Topology.Generation
```

```csharp
using Intropy.Topology.Generation;
```

The Aspire local-run backend and generation backend consume the same built model; they do not add topology facts.

## Vocabulary

| Term | Meaning |
|------|---------|
| System | One named integration system, declared by an `ISystemDefinition`. |
| SystemHost | The .NET project that contains the topology declaration and dispatches to run/generate/check commands. |
| Component | A declared integration block: extractor, loader, or transactional integration. |
| Message | A typed asynchronous contract moving over a Dapr pubsub topic. |
| Port | A named connection point between the system and the outside world. It materializes as a Dapr binding component with the same name. |
| Service | A Dapr app ID for a platform service that a component invokes. The topology records usage; it does not deploy the service. |
| OTLP | OpenTelemetry Protocol export settings for the whole system. |

## Complete declaration example

This example declares an `order-flow` system with one extractor, one loader, one message, two ports, one service dependency, and OTLP export.

```csharp
using Intropy.Topology;

public sealed record Order(string Id);

public static class Messages
{
    public static readonly MessageRef<Order> Orders =
        MessageRef<Order>.Define("orders", "pubsub");
}

public static class Ports
{
    public static readonly PortRef OrderExtractorSource =
        PortRef.Define("order-extractor-source");

    public static readonly PortRef OrderLoaderDestination =
        PortRef.Define("order-loader-destination");
}

public static class Services
{
    public static readonly ServiceRef Idempotency =
        ServiceRef.Define("idempotency-service");
}

public static class Components
{
    public const string OrderExtractor = "order-extractor";
    public const string OrderLoader = "order-loader";
}

public sealed class OrderFlowSystem : ISystemDefinition
{
    public string SystemName => "order-flow";

    public void Define(SystemBuilder builder)
    {
        builder.Otlp("http://otel-collector:4317")
            .WithProtocol(OtlpProtocol.Grpc)
            .WithHeader("x-api-key", "${OTLP_API_KEY}");

        builder.AddExtractor(Components.OrderExtractor)
            .From(Ports.OrderExtractorSource)
            .Publishes(Messages.Orders)
            .Uses(Services.Idempotency);

        builder.AddLoader(Components.OrderLoader)
            .Subscribes(Messages.Orders)
            .To(Ports.OrderLoaderDestination)
            .Uses(Services.Idempotency);
    }
}
```

`SystemDiscovery.Discover(assembly)` finds exactly one concrete `ISystemDefinition`, creates `SystemBuilder.Create(SystemName)`, calls `Define`, then builds and validates the topology. Tests or custom tools may use `SystemBuilder.Create(...)` directly:

```csharp
var builder = SystemBuilder.Create("order-flow");
new OrderFlowSystem().Define(builder);
SystemTopology topology = builder.Build();
```

## Name ownership and DNS rules

Names in the topology are minted facts. Backends and deployment consume them instead of maintaining second copies.

| Name | Rule | Used as |
|------|------|---------|
| System name | DNS-1123 label | message group name, system identity |
| Component name | DNS-1123 label | Kubernetes resource name and Dapr app ID |
| Message name | DNS-1123 subdomain | logical message identity |
| Pubsub name | DNS-1123 subdomain | Dapr pubsub component name |
| Topic name | DNS-1123 subdomain | Dapr pubsub topic name |
| Port name | DNS-1123 label | Dapr binding component name |
| Service app ID | DNS-1123 label | Dapr service invocation app ID |

Invalid names throw `ArgumentException` immediately at the declaration call site. Semantic problems across multiple declarations are collected during `Build()`.

## Reference declarations

References are usually static fields in scaffolded files such as `Messages.cs`, `Ports.cs`, `Services.cs`, and `Components.cs`. A reference alone does not create a resource; resources materialize only when components use the reference.

### Messages

A `MessageRef<T>` declares:

- a logical message name;
- a Dapr pubsub component name;
- a topic name within that pubsub;
- the payload contract type `T`.

```csharp
public static readonly MessageRef<Order> Orders =
    MessageRef<Order>.Define("orders", "pubsub");
```

The two-argument form uses the message name as the topic name. Use the three-argument form when the logical identity and transport topic differ:

```csharp
public static readonly MessageRef<Order> LegacyOrders =
    MessageRef<Order>.Define("legacy-orders", "pubsub", "orders-v1");
```

A message materializes when a component calls `Publishes(message)` or `Subscribes(message)`.

### Ports

A `PortRef` declares one system-owned outside-world connection point:

```csharp
public static readonly PortRef OrderExtractorSource =
    PortRef.Define("order-extractor-source");
```

The port name is the whole identity. The Dapr binding component name is identical to the port name. Direction is not part of the declaration; it follows from usage:

- `From(port)` means the component reads from the port.
- `To(port)` means the component writes to the port.

Binding type, address, credentials, and other environment details are deployment configuration, not topology.

### Services

A `ServiceRef` declares a Dapr app ID a component may invoke:

```csharp
public static readonly ServiceRef Idempotency =
    ServiceRef.Define("idempotency-service");
```

The service materializes when a component calls `Uses(service)`. The topology records consumers of the app ID; it does not define or deploy the provider.

### Components

Component names are plain strings and remain so — names are the identity, and the `Add*` builder returns a typed `Component` handle for in-definition references. A `Components.cs` holding the names as constants exists so the development definition can reference components by name without repeating a literal: `Rerun` validates the name against the topology, and a shared constant keeps a typo a compile error instead of a validation failure.

```csharp
public static class Components
{
    public const string OrderExtractor = "order-extractor";
    public const string OrderLoader = "order-loader";
}
```

## Component DSL grammar

`SystemBuilder` exposes block-specific builders. Each builder exposes only legal methods for that component kind, so many invalid topologies are compile-time errors.

| Entry point | Builder | Legal methods | Build-time requirements |
|-------------|---------|---------------|-------------------------|
| `AddExtractor(name)` | `ExtractorBuilder` | `From(port)`, `Publishes(message)`, `Uses(service)` | must publish at least one message |
| `AddLoader(name)` | `LoaderBuilder` | `Subscribes(message, when?, configure: sub => …)`, `To(port)`, `Uses(service)` | must subscribe to exactly one channel; `To` is optional |
| `Subscribes(message, when?, configure: sub => …)` | `SubscriptionBuilder` | `AlsoHandles(message, when?)`, `IgnoreOthers()`, `InBatches(maxMessages, maxWait)` | every handled message on one channel; see [Subscriptions](subscriptions.md) |
| `AddTransactionalIntegration(name)` | `TransactionalIntegrationBuilder` | `From(port)`, `To(port)`, `Uses(service)` | must have at least one `From` and at least one `To` |
| `Otlp(endpoint)` | `OtlpBuilder` | `WithProtocol(protocol)`, `WithHeader(name, value)` | only one OTLP declaration per system |

Every component builder also exposes `Component`, an optional typed handle with the declared component's `Name` and `Kind`.

### Extractor

An extractor pulls or receives data from outside the system and publishes messages.

```csharp
builder.AddExtractor("order-extractor")
    .From(Ports.OrderExtractorSource)
    .Publishes(Messages.Orders)
    .Uses(Services.Idempotency);
```

Rules:

- `From` is optional and may be called more than once.
- `Publishes` is required at least once.
- Publishing several distinct messages is legal.
- Publishing the same `(pubsub, topic)` channel more than once from the same component is a validation error.

### Loader

A loader subscribes to one channel — handling at least one of its messages — and may write to an
outside-world port.

```csharp
builder.AddLoader("order-loader")
    .Subscribes(Messages.Orders)
    .To(Ports.OrderLoaderDestination);
```

Rules:

- `Subscribes` is required exactly once.
- `To` is optional; a loader without `To` has a private local destination.
- Loaders cannot publish messages.

### Transactional integration

A transactional integration reads and writes external systems through ports. It does not publish public messages.

```csharp
builder.AddTransactionalIntegration("order-status-sync")
    .From(Ports.OrderExtractorSource)
    .To(Ports.OrderLoaderDestination)
    .Uses(Services.Idempotency);
```

Rules:

- At least one `From` is required.
- At least one `To` is required.
- Multiple input and output ports are legal.
- The model mints an internal queue named `internal-<component-name>` with topic `hop`. This queue is internal workload shape and does not appear in public `Topics`.

### Compile-time illegality

Illegal methods are absent from the builder types:

```csharp
builder.AddLoader("loader").Publishes(Messages.Orders);        // does not compile
builder.AddExtractor("extractor").Subscribes(Messages.Orders); // does not compile
```

## OTLP export DSL

`SystemBuilder.Otlp(endpoint)` declares one telemetry export target for every component and Dapr sidecar in the system.

```csharp
builder.Otlp("https://otel.example.com:4318")
    .WithProtocol(OtlpProtocol.HttpProtobuf)
    .WithHeader("authorization", "Bearer ${OTLP_TOKEN}");
```

Rules:

- The endpoint must be an absolute HTTP(S) URL.
- A system may declare OTLP at most once.
- Protocol defaults to `OtlpProtocol.Grpc`.
- Header names must be valid HTTP header tokens.
- Header values must be non-empty and contain no control characters.
- Secret values should be expressed as runtime placeholders such as `${OTLP_TOKEN}`; the topology does not resolve secrets.

If no OTLP declaration exists, `SystemTopology.Otlp` is `null` and omitted from JSON serialization.

## Materialized `SystemTopology`

`Build()` folds the mutable builder declarations into immutable records in `Intropy.Topology.Model`:

```csharp
public sealed record SystemTopology
{
    public required string SystemName { get; init; }
    public required IReadOnlyList<ComponentModel> Components { get; init; }
    public required IReadOnlyList<TopicResource> Topics { get; init; }
    public required IReadOnlyList<MessageGroupResource> MessageGroups { get; init; }
    public required IReadOnlyList<PortResource> Ports { get; init; }
    public required IReadOnlyList<ServiceResource> Services { get; init; }
    public OtlpSettings? Otlp { get; init; }
}
```

### Components

Each `Add*` call becomes one `ComponentModel` in declaration order.

| Field | Meaning |
|-------|---------|
| `Name` | Component name. |
| `Kind` | `Extractor`, `Loader`, or `TransactionalIntegration`. |
| `Subscribes` | Topic subscriptions as `(PubSubName, TopicName)`. |
| `Publishes` | Published topics as `(PubSubName, TopicName)`. |
| `Ports` | Used ports as `(PortName, Direction)`, where direction is `In` or `Out`. |
| `Uses` | Service app IDs invoked by this component. |
| `InternalQueue` | Transactional integration internal queue, otherwise `null`. |

### Topics and messages

A used message produces two views of the same asynchronous edge:

| Model | Purpose |
|-------|---------|
| `TopicResource` | Transport-oriented view: pubsub, topic, contract type, publishers, subscribers. |
| `MessageResource` | Message-identity view: name, contract type, channel, publishers, subscribers. |
| `MessageGroupResource` | Ownership boundary for messages. The current model creates one group named after the system. |

`Publishers` and `Subscribers` are precomputed sorted lists of component names. The contract type is recorded as the payload type's full name.

### Ports

Each used port becomes a `PortResource`:

| Field | Meaning |
|-------|---------|
| `Name` | Port identity. |
| `DaprComponentName` | Same as `Name`; this is the generated Dapr binding component name. |
| `Directions` | Union of used directions, `In` and/or `Out`. |
| `UsedBy` | Sorted list of components using the port. |

### Services

Each used service becomes a `ServiceResource`:

| Field | Meaning |
|-------|---------|
| `AppId` | Dapr app ID invoked by components. |
| `Consumers` | Sorted list of components invoking the service. |

### OTLP settings

If declared, `OtlpSettings` contains:

| Field | Meaning |
|-------|---------|
| `Endpoint` | The exact declared endpoint string. |
| `Protocol` | `Grpc` or `HttpProtobuf`. |
| `Headers` | Header names and values to pass to the runtime exporter. |

## Determinism

Materialization is deterministic so serialized topology JSON is stable across runs.

- Components keep declaration order.
- Topics are sorted by pubsub name, then topic name, using ordinal string comparison.
- Messages inside the message group are sorted by message name.
- Ports are sorted by port name.
- Services are sorted by app ID.
- Publishers, subscribers, port users, and service consumers are sorted sets.
- Materialization itself never fails; first-seen values are kept where conflicts exist so validation can report all semantic problems in one pass.

## Validation API

| API | Behavior |
|-----|----------|
| `Build()` | Materializes and validates. Returns `SystemTopology` when valid. Throws `TopologyValidationException` when any error-severity diagnostic exists. |
| `TryBuild(out topology, out diagnostics)` | Non-throwing build. Returns `false` on errors and provides all diagnostics, including warnings. |
| `Validate()` | Materializes and validates without returning the model. Provides all diagnostics, including warnings. |

`TopologyValidationException.Diagnostics` carries the full diagnostic list; the exception message lists every diagnostic rather than stopping at the first failure.

### Validation rules

| Severity | Rule |
|----------|------|
| Error | Component names must be unique. |
| Error | A system must declare at least one component. |
| Error | A component must not publish the same message more than once. |
| Error | A component must not subscribe to the same `(pubsub, topic)` channel more than once. |
| Error | One message name must not resolve to multiple channels. |
| Error | One message name must not carry multiple payload contract types. |
| Error | A subscription's messages must all travel on one channel. |
| Error | A component must not handle the same message more than once. |
| Error | Extractors must publish at least one message. |
| Error | Loaders must subscribe to exactly one channel. |
| Error | Transactional integrations must declare at least one `From` and one `To` port. |
| Error | A pubsub name must not equal a port's Dapr component name. |
| Error | A component must not use the same service more than once. |
| Error | A service app ID must not collide with a topology component app ID. |
| Warning | A published topic has no subscriber. |
| Warning | A subscribed topic has no publisher. |
| Warning | A component with no subscriptions, publishes, or ports is likely unfinished. |

Published-without-subscriber and subscribed-without-publisher are warnings in the core model so partially built systems can still run locally and be inspected. Deployment validation may choose to treat those warnings as errors.

## Local development DSL

A SystemHost may also declare local substitutions for ports and services. This DSL is validated against the already-built topology and cannot introduce new topology facts.

```csharp
using Intropy.Topology.Generation;

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

| Method | Meaning | Validation |
|--------|---------|------------|
| `Mock(service).FromOpenApi(path)` | Resolves a used service to a local OpenAPI-backed mock. | Service must be used by the topology; artifact must be OpenAPI 3.0.x, self-contained, readable, and inside the SystemHost directory. |
| `Files(port).RootPath(path)` | Resolves a used port to a local folder. | Port must be used by the topology; path must stay inside the SystemHost directory. |
| `Rerun(name).AfterEachRun(delay)` | Re-runs a run-to-completion component (extractor or transactional integration) once `delay` has passed after a run completes. | Name must be declared by the topology and belong to a run-to-completion component; delay must be positive and declared at most once. |

Every used port must have a local file resolution when a development definition is built. The re-run delay is local host mechanism only: it is never written into generated artifacts, so it cannot drift with the deployed schedule that deployment configuration owns.

## What consumers can rely on

A valid `SystemTopology` is:

- complete enough for generators to produce Dapr components and per-component runtime config;
- deterministic and suitable for JSON snapshots or diffing;
- free from collected error-severity validation diagnostics;
- explicit about ownership boundaries: topology-owned names are in the model, deployment-owned details are not.
