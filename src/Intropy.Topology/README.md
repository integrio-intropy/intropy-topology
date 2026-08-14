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
- **Model** — `SystemTopology` is the immutable, deterministic output; `TopologyValidationException` carries every diagnostic when validation fails.

## Companion packages

- `Intropy.Framework.*` — the runtime pipeline framework the declared components are built with

## License

MIT. See the [repository](https://github.com/integrio-intropy/intropy-topology) for source and documentation.
