# Intropy.Topology

A typed, fluent DSL for declaring integration-system topologies in .NET, and two backends that turn
a declaration into something you can run or deploy.

A system is a class implementing `ISystemDefinition`: it declares components (extractors, loaders,
transactional integrations) and the edges between them (topics, connectors). `Build()` validates the
declaration and produces an immutable, serializable `SystemTopology`. From that one validated model,
the backends generate Dapr components / runtime config, or translate it into a live .NET Aspire
application for local F5.

> This is the **minimal model**: exactly the surface the system tutorial and `intropy sys create`
> use. The full model (aggregators, APIs, named publish ports, sftp/blob/http/custom transports,
> dev mocking) is preserved on the `full-topology` branch.

## Why?

The shape of an integration system — which components exist, which topics and external systems they
touch — is usually buried across Kubernetes manifests and Dapr YAML, where nothing checks it.
Intropy.Topology moves that declaration into typed code: each `Add*` call returns a block-specific
builder that exposes only the edges legal for that block. *Illegal topology is a compile error, not
a validation diagnostic*; only what types cannot check (completeness and cross-component conflicts)
is validated at `Build()`.

The topology records the edges **between** components — subscriptions, publishes, and connectors
out to the outside world — plus one activation attribute: an optional cron schedule
(`WithSchedule`, extractors only). Everything else about the workload shape lives in the
component's own scaffold.

## Packages

| Package | Description |
|---------|-------------|
| [Intropy.Topology](https://www.nuget.org/packages/Intropy.Topology) | Staged builders, refs, discovery, the immutable topology model, and validation |
| Intropy.Topology.Generation | `SystemTopology` → Dapr component YAML + per-component runtime config; the `check`/`graph`/`generate` verbs |
| Intropy.Topology.Aspire | Translates a discovered topology into a live .NET Aspire application (Redis-backed pub/sub, Dapr sidecars) |

## Quick start

Declare the system as an `ISystemDefinition`:

```csharp
public sealed class OrderFlowSystem : ISystemDefinition
{
    public string SystemName => "order-flow";

    public void Define(SystemBuilder builder)
    {
        builder.AddExtractor("order-extractor")
            .From(Connectors.OrderExtractorSource)
            .Publishes(Topics.Orders);

        builder.AddLoader("order-loader")
            .Subscribes(Topics.Orders)
            .To(Connectors.OrderLoaderDestination);
    }
}
```

Topics and connectors are declared as static fields; the backing resources materialize from usage
(there is no `AddTopic`). The Dapr binding component name is derived from the connector's name
(`binding.order-extractor-source`), never declared:

```csharp
public static class Topics
{
    public static readonly TopicRef<Order> Orders = TopicRef<Order>.Define("pubsub", "orders");
}

public static class Connectors
{
    public static readonly ConnectorRef OrderExtractorSource =
        ConnectorRef.Define("order-extractor-source", Transport.File("./test/order-extractor-source"));
    public static readonly ConnectorRef OrderLoaderDestination =
        ConnectorRef.Define("order-loader-destination", Transport.File("./test/order-loader-destination"));
}
```

All connectivity runs through Dapr — a transport selects the Dapr binding component's `spec.type`.
The minimal model ships one transport: `Transport.File(rootPath)` (`bindings.localstorage`), the
default resolution `intropy sys create` applies. The folder is the endpoint, so a file connector
needs no external-system concept — the system runs with zero external configuration.

## One model, two backends

The SystemHost has a single entry point; the same discovered, validated topology feeds either backend:

```csharp
var assembly = Assembly.GetExecutingAssembly();
return args is ["run", ..] or []
    ? await IntropyAspire.RunAsync(assembly, args)   // → .NET Aspire + DCP (local F5)
    : await IntropyGenerate.RunAsync(assembly, args); // → generate | check | graph
```

```bash
dotnet run --project examples/OrderFlow.SystemHost                     # Aspire dashboard (needs Docker + `dapr init`)
dotnet run --project examples/OrderFlow.SystemHost -- check            # validate the topology
dotnet run --project examples/OrderFlow.SystemHost -- graph            # print the topology.intropy.io/v1 JSON document
# `dotnet run` prints its own build and launch-profile messages before starting the host. For a machine-readable stream:
dotnet run --no-build --no-launch-profile --project examples/OrderFlow.SystemHost -- graph  # build first, then JSON only on stdout
dotnet run --project examples/OrderFlow.SystemHost -- generate ./out   # write Dapr YAML + per-component config
```

The Aspire backend is a one-way **translator**: it discovers the model, generates the system's Dapr
components, and speaks Aspire's language (`AddProject`, `AddContainer`, `WithDaprSidecar`) to build
the resource graph DCP runs — Redis behind pub/sub, one project with a Dapr sidecar per component,
subscribers started before their publishers. Each sidecar loads a staged overlay: the integration's
own `local/dapr-components/` plus the generated system components, where a generated component with
the same `metadata.name` replaces the local one. Aspire never sees the model — the same one-way
relationship Generation has with Dapr YAML, targeting a live object model instead of files. So F5
and `kubectl apply` become two backends of the same generated truth.

A complete, runnable example lives in [`examples/OrderFlow.SystemHost`](examples/OrderFlow.SystemHost/).
It maps each component to a sibling worker project by folder convention (`order-extractor` →
`../order-extractor/`), and its declaration files mirror `intropy sys create` output verbatim.

## Illegal topology does not compile

The block-specific builders expose only each block's legal edges, so these are compile errors — the
method simply does not exist:

```csharp
builder.AddLoader("l").Subscribes(t).Publishes(t);   // loaders publish nothing
builder.AddExtractor("e").Subscribes(t);             // extractors read connectors, not topics
```

Per block: extractors read a connector (`From`) and publish one topic; loaders subscribe to one
topic and may write through a connector (a private local destination needs no declared edge);
transactional integrations read/write connectors. Every topic must have both sides: `Build()`
rejects a published topic nobody subscribes to and a subscription nothing publishes.

## Requirements

- .NET 10 or later
- For the Aspire `run` backend: Docker and the Dapr CLI (`dapr init`)

## Documentation

Full documentation lives in [`docs/`](docs/index.md):

- [Getting Started](docs/getting-started.md) — declare a complete order-flow topology
- [Components](docs/concepts/components.md) — component kinds and block builders
- [Topics](docs/concepts/topics.md) and [Connectors](docs/concepts/connectors.md) — refs, transports, materialize-from-usage
- [Validation](docs/concepts/validation.md) — the validation rules and Build/TryBuild/Validate
- [Materialization](docs/concepts/materialization.md) — the immutable, deterministic output model

## Build and test

```bash
dotnet build Intropy.Topology.slnx
dotnet test Intropy.Topology.slnx
```

## License

[MIT](LICENSE) © Integrio
