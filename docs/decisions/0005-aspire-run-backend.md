# 0005: Aspire run-backend — discover → generate → translate

> Status: implemented.

**Goal:** run a declared system locally with a single F5, without Aspire ever seeing the domain
model, and without any Aspire dependency in the core library. F5 and `kubectl apply` should be two
backends of the *same* generated truth.

## The one-way translator

The AppHost has one entry point that routes to one of two backends over the *same* discovered,
validated topology:

```csharp
var assembly = Assembly.GetExecutingAssembly();
return args is ["run", ..] or []
    ? await IntropyAspire.RunAsync(assembly, args)     // → .NET Aspire + DCP
    : await IntropyGenerate.RunAsync(assembly, args);  // → generate | check | graph
```

`IntropyAspire.RunAsync` is a translator, not an integration Aspire is aware of:

1. **discover** — `SystemDiscovery.Discover(assembly)` finds the one `ISystemDefinition`, builds and
   validates its topology (so F5 fails on an invalid topology before anything starts).
2. **generate** — `TopologyGenerator.Generate(topology, Dev, devManifest)` writes Dapr component
   YAML + per-component runtime config to a temp folder, in-memory-equivalent to what `generate`
   writes to disk.
3. **translate** — walk the model and speak Aspire's language: a plain Redis container behind
   pub/sub (not `AddRedis`, which fronts Redis with TLS the generated YAML doesn't speak),
   `AddContainer` for declared mock external systems, `AddProject` (by folder convention) or a
   dev-override container per component, each `WithDaprSidecar` (`app-id` = component name,
   `--resources-path` = the generated components dir).

Aspire receives ordinary resources and never learns a domain model produced them — the same one-way
relationship Generation has with Dapr YAML, targeting a live object model instead of files.

## What changed

- **Core (`Intropy.Topology`, still dependency-free):** added `ISystemDefinition`
  (`SystemName` + `Define(SystemBuilder)`) and `SystemDiscovery.Discover(Assembly)` →
  `DiscoveredSystem`. A system is now a discoverable type, not an inline script.
- **New `Intropy.Topology.Generation` (dependency-free):** `GenerationProfile` (Dev/Prod),
  `TopologyGenerator` → `GeneratedArtifacts` (Dapr pub/sub + binding YAML, per-component
  `*.intropy.json`), a hand-rolled minimal YAML writer, the `IntropyGenerate` CLI verbs
  (`check`/`graph`/`generate`), and the `IDevProfile`/`DevBuilder`/`DevManifest` dev seam.
- **`Intropy.Topology.Aspire` (Aspire dependency isolated here):** replaced the hand-mapping
  `AddIntropySystem`/`Map` surface with `IntropyAspire.RunAsync` + the folder-convention
  `ProjectConvention`.
- **Example:** the reference project is an Aspire AppHost again (`OrderFulfillment.AppHost`, with
  `Aspire.AppHost.Sdk`), with a shared `OrderFulfillment.Contracts` library and five minimal worker
  projects resolved by folder convention. This reverses the `AppHost`→`SystemHost` rename recorded
  in ADR 0004 §Naming: the single project both declares the system and hosts both backends.

  > **Amended 2026-07-24:** reversed again — the user-facing name is **SystemHost** (matching the
  > CLI noun `intropy sys create` and the tutorial); the example is `OrderFulfillment.SystemHost`.
  > "Aspire AppHost" remains the plumbing term: the SystemHost is an ordinary console project
  > holding the declaration, and the `Aspire.AppHost.Sdk` line in its csproj exists so the run
  > backend can fetch the orchestrator and dashboard.

## Decisions

- **Dev mocks/overrides live on the definition** via `IDevProfile`, captured into a `DevManifest`
  that never enters `SystemTopology` — the prod model stays pure.
- **Folder convention** binds a component to its project: `order-extractor` → `../order-extractor/`.
- **Dev pub/sub is Redis** on a fixed host port (6379); generated YAML points there.

  > **Amended 2026-07-25:** the dev broker is now **RabbitMQ** (`pubsub.rabbitmq`) on fixed host
  > port **5672**, default `guest`/`guest` (which RabbitMQ itself restricts to localhost — exactly
  > how the sidecars connect). The shape is unchanged: a plain container rather than
  > `AddRabbitMQ`, which injects generated credentials the generated YAML doesn't know.

## Known limits

- **No Hosting framework yet.** The injected runtime-config contract (`INTROPY__COMPONENT`,
  `INTROPY__CONFIG` → `*.intropy.json`) has no in-repo consumer; the example workers hand-read it.
  It is provisional and must be re-aligned when intropy-framework lands.
- **Generate-before-run** forces fixed dev host ports (Redis 6379, per-mock ports); port conflicts
  are possible.
- ~~**HTTP connectors have no local stand-in**~~ — resolved by ADR 0006: contract-backed Microcks
  mocks serve `bindings.http` connectors (and, optionally, internal APIs) in dev.
- **Prod generation is a placeholder** — `Prod` emits backend-neutral components. Real prod
  generation (K8s workloads + prod Dapr components) remains downstream; this backend deliberately
  duplicates a minimal dev slice of it.
- **Triggers/schedules stay out** (ADR 0004): there is no cron in the model, so no local timer here.
  When triggers return, an in-process Hosting timer is the answer, not an adapter fake.
