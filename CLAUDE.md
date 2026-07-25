# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Test Commands

```bash
# Build the solution
dotnet build Intropy.Topology.slnx

# Run all tests
dotnet test Intropy.Topology.slnx

# Run a single test class
dotnet test Intropy.Topology.slnx --filter "FullyQualifiedName~ClassName"

# Run a specific test
dotnet test Intropy.Topology.slnx --filter "FullyQualifiedName=Namespace.ClassName.TestMethodName"

# Run the reference SystemHost (F5 backend; needs Docker + `dapr init`)
dotnet run --project examples/OrderFlow.SystemHost

# Inspect the same declaration without running anything
dotnet run --project examples/OrderFlow.SystemHost -- check      # validate
dotnet run --project examples/OrderFlow.SystemHost -- graph      # print the model
dotnet run --project examples/OrderFlow.SystemHost -- generate ./out
```

## Architecture Overview

This is a typed, fluent DSL for declaring Intropy integration-system topologies in .NET 10. A SystemHost project declares a system's components and the edges between them in code; `Build()` validates the declaration and produces an immutable, serializable `SystemTopology` that downstream Kubernetes/Dapr generation tooling consumes. The library has zero external runtime dependencies.

**This is the minimal model** — exactly what the system tutorial and `intropy sys create` need. The full model (aggregators, APIs/`Provides`/`Consumes`, named publish ports, Http/Sftp/Blob/Custom transports, unresolved connectors, the `IDevProfile` mocking seam, the Prod generation profile) is preserved on the **`full-topology`** branch for later reapplication; don't re-add those features here piecemeal — cherry-pick from that branch.

**The topology is only the edges *between* components** — subscriptions, publishes, connectors. What activates a component at runtime (cron schedule vs. long-running app) and its workload shape are *not* modeled here; they live in the component's own scaffold. There is no trigger concept and no `WorkloadType`.

### Packages

| Package | Purpose |
|---------|---------|
| **Intropy.Topology** | Staged builders, refs, the immutable topology model, validation, and `ISystemDefinition` discovery (dependency-free) |
| **Intropy.Topology.Generation** | `SystemTopology` → Dapr component YAML + per-component runtime config; the `check`/`graph`/`generate` verbs (dependency-free) |
| **Intropy.Topology.Aspire** | The F5 run backend: translates the discovered topology into Aspire resources (per-integration component overlay under `obj/dapr-components/`, subscriber-first startup ordering); the only package with an Aspire dependency |

The user-facing name for the host project is **SystemHost** (`OrderFlow.SystemHost`): an ordinary console project holding the system declaration, whose `Program.cs` routes to the Aspire or generate backend. "Aspire AppHost" is plumbing — the `Aspire.AppHost.Sdk` csproj line exists so the run backend can fetch the orchestrator and dashboard (ADR 0005, amended).

The example SystemHost's `Topics.cs` / `Connectors.cs` / `OrderFlowSystem.cs` mirror `intropy sys create` codegen **verbatim** (golden output in intropy-cli's `internal/system/create_test.go`) — they double as a compile-time guarantee that CLI-generated code builds against this library. Keep them in sync with the CLI templates.

### Key Concepts

**Per-block builders = compile-time legality**: each `Add*` on `SystemBuilder` returns that block's builder, whose members are exactly the edges legal for that block — an extractor has `From`/`Publishes`, a loader has `Subscribes`/`To`, a transactional integration has `From`/`To`. Declaring an edge a block cannot have is a compile error, not a validation diagnostic. Retired diagnostic codes — never reuse: ITP201/202/203 (triggers), ITP204/ITP205 (compile-time), and — retired with the minimal model — ITP105/ITP106 (connector direction capability), ITP107 (unresolved connector), ITP501 (API provider).

**Edges, not triggers**: async interfaces are topics (`Publishes`/`Subscribes`); connectors (`From`/`To`) are the edges *out* to the outside world. Every topic must have both sides — a published topic with no subscriber is ITP208, a subscription nothing publishes is ITP209 (both errors). A component publishes at most one topic (ITP101 — named ports live on the `full-topology` branch). A loader needs no `To`: its destination may stay a private local component (decision 0008). The block kinds are Extractor, Loader, and TransactionalIntegration.

**Component = the shared substance behind every block** (mirrors `Pipeline` in intropy-framework): `public abstract Component` (root namespace) is the recording state every builder writes into, and each block kind is a sealed subclass (`ExtractorComponent`, `LoaderComponent`, `TransactionalIntegrationComponent`) exposed via the builder's `.Component` property as an optional typed handle (public surface: `Name`, `Kind`; recorded calls stay internal). The hierarchy lives in `Components.cs`; all builders live in `Building/ComponentBuilders.cs` (grouped files — related types share a file rather than one type per file). See `docs/decisions/0003-per-block-components.md` (layout section amended 2026-07-19).

**Refs declared as static fields**: `TopicRef<T>.Define(pubsub, topic)` and `ConnectorRef.Define(name, transport)` live in scaffolded `Topics.cs` / `Connectors.cs` files in the SystemHost. The only transport is `Transport.File(rootPath)` (`bindings.localstorage`) — the default resolution `intropy sys create` applies; a file connector's folder is its endpoint, so no external-system concept is needed. Resources (topics, connectors) materialize from usage — there is no `AddTopic`.

**Declare → materialize → validate**: `TopologyMaterializer` (in `src/Intropy.Topology/Building/`) never fails (first-seen wins, deterministic ordering: declaration order for components, `StringComparer.Ordinal` sorting for resources). `TopologyRules` (in `src/Intropy.Topology/Validation/`) then collects every diagnostic; `Build()` throws `TopologyValidationException` carrying all of them when any is error-severity. `TryBuild`/`Validate` are the non-throwing variants.

**Namespaces**: the flat `Intropy.Topology` namespace is the user-facing DSL surface (project root, including `Component` and the per-block components); `Intropy.Topology.Building` holds the fluent machinery users never name; `Intropy.Topology.Model` is the output model; `Intropy.Topology.Validation(.Rules)` is internal.

**File layout is grouped, not one-type-per-file**: cohesive types share a file (`Components.cs`, `Refs.cs`, `Building/ComponentBuilders.cs`, `Model/Edges.cs`, `Model/Resources.cs`, `Model/Enums.cs`). When adding a type, put it in the existing file it belongs with; only give it its own file when no grouping fits.

**Names are DNS-1123**: component/topic/connector names are validated eagerly at the call site by `NameRules` (`ArgumentException`); only semantic topology validation is deferred to `Build()`.

### Testing

Tests use xUnit with plain `Assert` (no mocking or assertion libraries). Test method naming follows `Method_WithCondition_ShouldExpectation`, with explicit `// Arrange / // Act / // Assert` comments. Test files mirror the source structure under `test/Intropy.Topology.Test/`; internals are reachable via `InternalsVisibleTo`. `EndToEndTests` is the living spec and includes a **byte-exact JSON snapshot** of the materialized model — deliberate model changes require updating the inline snapshot.
