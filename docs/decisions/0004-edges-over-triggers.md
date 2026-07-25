# Refactoring task: edges over triggers, and APIs as first-class edges

> Status: implemented — see the "PoC lean pass" section below for features
> subsequently removed (Reconciler, ContainerService, platform services).

**Goal:** simplify the DSL so the topology models *only the edges between components*.
Remove the trigger concept entirely; add the API as the synchronous, intra-system sibling
of the topic. This supersedes the trigger design in
`0001-staged-per-block-builders.md`.

## Rationale

The trigger concept conflated two unrelated things:

1. **Inbound edges** — a topic subscription (`Trigger.Topic`) and a connector input
   (`Trigger.Connector`) were genuinely *between* things, wearing a trigger costume.
2. **Runtime activation / workload shape** — the cron expression and the resulting
   `CronJob` vs. `Deployment` choice are purely *inside* a component. Nothing between two
   components changes whether an extractor wakes on a timer or long-polls.

Only (1) belongs in a topology. So:

- `Trigger.Topic(...)` → `.Subscribes(...)` — a plain subscription edge.
- `Trigger.Connector(...)` → collapses into `.From(...)` (also removes the redundancy of
  passing one connector to both the trigger and `From`).
- `Trigger.Schedule(...)` / `Trigger.Http()` → gone. Cron and workload shape move to the
  component's own scaffold. HTTP-reachability is re-expressed as **providing an API**.

## What changed

- **Deleted:** `Trigger` (+ subtypes), `Model/TriggerModel.cs`, `WorkloadType`,
  `TriggerKind`, `CronValidator`, and the `*TriggerStage` builder types. `Add*` now returns
  the block builder directly.
- **Added:** `ApiRef<T>` (declared as static fields like `TopicRef`), `.Provides(api)` on
  extractors and transactional integrations, `.Consumes(api)` on every builder, and
  `ApiResource` in the model. An API has exactly one provider and any number of consumers.
- **Model:** `ComponentModel` drops `WorkloadType`/`Trigger` and gains `Subscribes`,
  `Provides`, `Consumes`; `SystemTopology` gains `Apis`.

## Interface model

| Interface | Between | Provider in topology? | Style |
|-----------|---------|-----------------------|-------|
| Topic     | component ↔ component | many publishers | async |
| API       | component ↔ component | exactly one     | sync  |
| Connector | component ↔ external system | none (external) | binding |
| Service   | component ↔ external platform svc | none (external) | sync |

Calling an *external* system's HTTP endpoint stays a connector (`.To` with HTTP transport);
`Provides`/`Consumes` are only for the system's own components.

## Validation

- Retired (do not reuse): `ITP201` (invalid cron), `ITP202` (missing trigger), `ITP203`
  (multiple `TriggeredBy`). `ITP204`/`ITP205` remain compile-time.
- `ITP102` now means "duplicate subscription" (was "duplicate trigger topic").
- Added `ITP207` (aggregator/reconciler must subscribe ≥1; loader exactly 1) and `ITP501`
  (an API must have exactly one provider).

## Naming

The reference example project is `OrderFulfillment.SystemHost` (was `.AppHost`); the term
for a project that declares a system's topology is **SystemHost**.

## PoC lean pass (2026-07-21)

To keep the PoC minimal, three features were removed the same day, to be re-added when a
concrete need appears (git history + this ADR preserve the design):

- **Reconciler** block (was a structural clone of Aggregator).
- **ContainerService** block (+ `ComponentModel.Image`/`Endpoints`/`Environment`,
  `EndpointModel`, `EnvironmentVariableModel`).
- **Platform services**: `ServiceRef`, `UseService`, `.Uses`, `ServiceResource`,
  `ServiceUseEdge`, `SystemTopology.Services`, and diagnostics `ITP301`/`ITP402`.

Kept: Extractor, Aggregator, Loader, TransactionalIntegration, and the full API concept
(`ApiRef`, `Provides`/`Consumes`, `ITP501`).
