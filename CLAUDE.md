# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Test Commands

```bash
dotnet build Intropy.Topology.slnx
dotnet test Intropy.Topology.slnx
dotnet test Intropy.Topology.slnx --filter "FullyQualifiedName~ClassName"

# Run the reference SystemHost (F5 backend; needs Docker + `dapr init`)
dotnet run --project examples/OrderFlow.SystemHost

# Inspect the same declaration without running anything
dotnet run --project examples/OrderFlow.SystemHost -- check      # validate
dotnet run --project examples/OrderFlow.SystemHost -- graph      # print the model
dotnet run --project examples/OrderFlow.SystemHost -- generate ./out
```

## Architecture

A typed, fluent DSL for declaring Intropy integration-system topologies in .NET 10. A SystemHost project declares a system's components (Extractor, Loader, TransactionalIntegration) and the edges between them; `Build()` validates the declaration and produces an immutable, serializable `SystemTopology` that downstream Kubernetes/Dapr generation tooling consumes. The core library has zero external runtime dependencies. This is the minimal model; the full model (aggregators, APIs, named ports, more transports) lives on the `full-topology` branch — cherry-pick from there rather than re-adding features piecemeal.

| Package | Purpose |
|---------|---------|
| **Intropy.Topology** | Builders, refs, the immutable topology model, validation, and `ISystemDefinition` discovery (dependency-free) |
| **Intropy.Topology.Generation** | `SystemTopology` → Dapr component YAML + per-component runtime config; the `check`/`graph`/`generate` verbs (dependency-free) |
| **Intropy.Topology.Aspire** | The F5 run backend: translates the discovered topology into Aspire resources; the only package with an Aspire dependency |

Key invariants:

- **Per-block builders = compile-time legality**: each `Add*` on `SystemBuilder` returns that block's builder, whose members are exactly the edges legal for that block. Illegal topology is a compile error; only what types cannot check (completeness, cross-component conflicts) is validated at `Build()`, which collects every diagnostic before throwing (`TryBuild`/`Validate` are the non-throwing variants).
- **Edges, not triggers**: topics (`Publishes`/`Subscribes`) and connectors (`From`/`To`) are the whole topology, plus one activation attribute — an optional cron schedule (`WithSchedule`, extractors only). Every topic needs both a publisher and a subscriber; a component publishes at most one topic; a loader's `To` is optional.
- **Resources materialize from usage**: `TopicRef<T>.Define` / `ConnectorRef.Define` are declared as static fields in scaffolded `Topics.cs` / `Connectors.cs`; there is no `AddTopic`. Materialization never fails (first-seen wins, deterministic ordering).
- **Names are DNS-1123**, validated eagerly at the call site by `NameRules` (`ArgumentException`); only semantic validation is deferred to `Build()`.
- **Namespaces**: flat `Intropy.Topology` is the user-facing DSL surface; `.Building` is fluent machinery users never name; `.Model` is the output model; `.Validation(.Rules)` is internal.
- **File layout is grouped, not one-type-per-file**: cohesive types share a file (`Components.cs`, `Refs.cs`, `Building/ComponentBuilders.cs`, `Model/Resources.cs`). Put new types in the existing file they belong with.

The example SystemHost's `Topics.cs` / `Connectors.cs` / `OrderFlowSystem.cs` mirror `intropy sys create` codegen **verbatim** (golden output in intropy-cli's `internal/system/create_test.go`) — they double as a compile-time guarantee that CLI-generated code builds against this library. Keep them in sync with the CLI templates.

## Testing

Tests use xUnit with plain `Assert` (no mocking or assertion libraries). Test method naming follows `Method_WithCondition_ShouldExpectation`, with explicit `// Arrange / // Act / // Assert` comments. Test files mirror the source structure under `test/Intropy.Topology.Test/`; internals are reachable via `InternalsVisibleTo`. `EndToEndTests` is the living spec and includes a **byte-exact JSON snapshot** of the materialized model — deliberate model changes require updating the inline snapshot.
