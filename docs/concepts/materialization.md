# Materialization

> Builder declarations fold into an immutable, deterministic model — components in declaration order, resources deduplicated from usage and sorted.

## How it works

```mermaid
graph TD
    B["SystemBuilder declarations"] --> M["TopologyMaterializer"]
    M --> C["Components (declaration order)"]
    M --> T["Topics (from usage, sorted)"]
    M --> K["Connectors (from usage, sorted)"]
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
    public required IReadOnlyList<ConnectorResource> Connectors { get; init; }
}
```

| Model | Materialized from |
|-------|-------------------|
| `ComponentModel` | Each `Add*` call, in declaration order, with its edges (`Subscribes`, `Publishes`, `Connectors`) |
| `TopicResource` | Every topic published to or subscribed from, with `Publishers`/`Subscribers` precomputed |
| `ConnectorResource` | Every connector used, with the union of directions and `UsedBy` |

A `ConnectorResource` is `{ Name, DaprComponentName, Directions, UsedBy }` — the name is the whole identity, and `DaprComponentName` is derived (`binding.<name>`), never stored.

## Determinism

The materializer is deliberately deterministic so the serialized model is byte-stable across runs:

- Components keep declaration order.
- Topics and connectors are sorted with `StringComparer.Ordinal`.
- `Publishers`, `Subscribers`, and `UsedBy` lists are sorted sets.

A byte-exact JSON snapshot test guards this — the same declaration always serializes to the same JSON, which downstream generation tooling can diff and cache.

## Serialization

The model round-trips through `System.Text.Json`:

```json
{
  "Name": "order-loader-destination",
  "DaprComponentName": "binding.order-loader-destination",
  "Directions": [1],
  "UsedBy": ["order-loader"]
}
```

## Related

- [Validation](validation.md) — the rules that run over the materialized picture
- [Topics](topics.md) and [Connectors](connectors.md) — the resource models
