# Materialization

> Builder declarations fold into an immutable, deterministic model — components in declaration order, resources deduplicated from usage and sorted.

## How it works

```mermaid
graph TD
    B["SystemBuilder declarations"] --> M["TopologyMaterializer"]
    M --> C["Components (declaration order)"]
    M --> T["Topics (from usage, sorted)"]
    M --> K["Ports (from usage, sorted)"]
    M --> S["Services (from usage, sorted)"]
    M --> O["OTLP (optional system setting)"]
```

`Build()` first folds the recorded declarations into the immutable `SystemTopology`. Materialization never fails — structurally broken declarations still materialize (first-seen wins on conflicts) so validation rules can inspect and report the full picture afterwards.

## The model

Everything in `Intropy.Topology.Model` is a sealed record with `required` init-only members and `IReadOnlyList<T>` collections:

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

| Model | Materialized from |
|-------|-------------------|
| `ComponentModel` | Each `Add*` call, in declaration order, with its edges (`Subscribes`, `Publishes`, `Ports`) |
| `TopicResource` | Every message channel published to or subscribed from, with `Publishers`/`Subscribers` precomputed |
| `MessageGroupResource` | One group named after the system, holding a `MessageResource` per declared message — identity, contract, channel, and both sides |
| `PortResource` | Every port used, with the union of directions and `UsedBy` |
| `ServiceResource` | Every service app ID used, with `Consumers` |
| `OtlpSettings` | The optional system-wide OTLP endpoint, protocol, and headers |

A `PortResource` is `{ Name, DaprComponentName, Directions, UsedBy }` — the name is the whole identity, and `DaprComponentName` repeats it (the binding takes the port name unchanged). A `ServiceResource` is `{ AppId, Consumers }` — it records an invocation dependency, not something the topology deploys.

## Determinism

The materializer is deliberately deterministic so the serialized model is byte-stable across runs:

- Components keep declaration order.
- Topics, ports, services, and messages inside the message group are sorted with `StringComparer.Ordinal`.
- `Publishers`, `Subscribers`, `UsedBy`, and `Consumers` lists are sorted sets.

A byte-exact JSON snapshot test guards this — the same declaration always serializes to the same JSON, which downstream generation tooling can diff and cache.

## Serialization

The model round-trips through `System.Text.Json`:

```json
{
  "Name": "order-loader-destination",
  "DaprComponentName": "order-loader-destination",
  "Directions": [1],
  "UsedBy": ["order-loader"]
}
```

## Related

- [Model and DSL Reference](model.md) — the full model shape and declaration grammar
- [Validation](validation.md) — the rules that run over the materialized picture
- [Messages](messages.md) and [Ports](ports.md) — the resource models
