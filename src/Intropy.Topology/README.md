# Intropy.Topology

Typed, fluent DSL for declaring Intropy integration-system topologies in .NET.

This package provides the per-block builders, refs, and the immutable topology model. Use it in a SystemHost project to declare a system's components and the edges between them in code; `Build()` validates the declaration and produces a serializable `SystemTopology` that downstream Kubernetes and Dapr generation tooling consumes.

## Install

```bash
dotnet add package Intropy.Topology
```

## Concepts

- **Components** — extractors, loaders, and transactional integrations, each declared through a builder that exposes only its legal edges. Illegal topology is a compile error, not a validation diagnostic.
- **Edges** — the topology is the wiring *between* components: async topics (`Publishes`/`Subscribes`), external ports (`From`/`To`), and service uses. The rest of the workload shape, including activation, lives in the component's scaffold.
- **Refs** — `TopicRef<T>`, `PortRef`, and `ServiceRef` name the things components connect to. Resources materialize from usage — there is no `AddTopic`.
- **Telemetry** — `builder.Otlp(endpoint)` declares the OTLP sink the whole system (every component and its Dapr sidecar) exports telemetry to; the Kubernetes generation translates the declaration into the standard `OTEL_EXPORTER_OTLP_*` variables. Declaring nothing keeps each runtime's default sink. Header values are passed through verbatim, so environment placeholders such as `${OTLP_API_KEY}` are resolved by the runtime — declare secrets only as placeholders. The local Aspire host deliberately does not route the declaration; it runs with sidecar telemetry disabled and Aspire's own dashboard wiring.
- **Model** — `SystemTopology` is the immutable, deterministic output; `TopologyValidationException` carries every diagnostic when validation fails.

## Companion packages

- `Intropy.Framework.*` — the runtime pipeline framework the declared components are built with

## License

MIT. See the [repository](https://github.com/integrio-intropy/intropy-topology) for source and documentation.
