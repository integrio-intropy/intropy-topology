# Schedule as an activation attribute

> Status: accepted (2026-07-25). Narrows [ADR 0004](0004-edges-over-triggers.md): the
> trigger concept stays dead, but one activation fact — the cron schedule — returns to the
> model as a component *attribute*, not an edge.

## Context

The extractor scaffold became a run-to-completion job (one sweep per run, then exit; a
Kubernetes CronJob in production). That removed scheduling from the block, and the
schedule needs a home the whole system can see: the SystemHost must re-run the block
locally, and the future generation backend must emit a CronJob manifest. ADR 0004 removed
`Trigger.Schedule(...)` because triggers conflated inbound *edges* with runtime
activation; that reasoning still holds for edges — but the cron expression itself turned
out to be a fact the topology's consumers need, not a scaffold-private detail.

## Decision

- `ExtractorBuilder.WithSchedule("<cron>")` — optional; extractor-only at **compile
  time** (the method exists only on `ExtractorBuilder`). Without a schedule the extractor
  runs once at host start, exactly as before. A later call replaces the value.
- Model: `ComponentModel.Schedule : string?`. No `WorkloadType`, no trigger types — the
  workload shape still lives in the component's scaffold; the topology records one
  activation attribute.
- Validation: null/whitespace throws `ArgumentException` eagerly at the call site;
  cron *syntax* is checked at `Build()` by the resurrected shallow `CronValidator`
  (five fields or `@daily`-style macros) as **ITP210**. ITP201 — the trigger-era code —
  stays retired. The check is deliberately shallow: the run backend (Cronos) and
  Kubernetes are the authority on full cron semantics.
- Generation: the schedule is surfaced in `config/<component>.intropy.json` and the
  `graph` verb. CronJob YAML emission stays out of scope (no workload generation exists).
- Dependencies: Cronos lives **only** in `Intropy.Topology.Aspire`;
  `Intropy.Topology` and `Intropy.Topology.Generation` stay dependency-free.

## Run-backend semantics (deliberate divergences from Kubernetes CronJob)

The Aspire run backend's `ComponentScheduler` re-runs scheduled components on their cron
ticks. Where it diverges from a real CronJob, it diverges toward the F5 dev loop:

- **Runs once at host start, then on each tick.** Resources keep normal auto-start (a
  real CronJob does nothing until its first tick); the scheduler only owns the re-runs.
- **Local time zone**, not UTC: a developer writing `0 9 * * *` expects 9 a.m. on their
  wall clock, which is also the dashboard's clock.
- **Overlap = skip with a warning** (`concurrencyPolicy: Forbid`): a tick that lands
  while the previous run is still in progress is dropped, never queued or doubled.
- **"Run now" dashboard command** on every scheduled component, sharing the tick path —
  including the skip-on-overlap rule.
- **Sidecar first.** A run-to-completion block shuts its own Dapr sidecar down when it
  exits (the scaffold's `ShutdownSidecarAsync`), and the toolkit's `<name>-dapr-cli`
  sidecar is an independent resource that does not restart with the app — so each tick
  starts the sidecar, waits for Running, then starts the app.
- **No HTTP endpoint** for scheduled components: a run-to-completion job receives no
  inbound delivery (extractors cannot `Subscribes` by construction), so it gets neither
  an Aspire endpoint nor a sidecar app-port. This is keyed off `Schedule != null`, never
  the component kind — the scheduler is kind-blind.

## Out of scope / future

- Kubernetes CronJob YAML generation (no workload generation exists at all yet).
- `intropy sys create` codegen emitting `WithSchedule` (intropy-cli repo) — until then
  the example host's `WithSchedule` line deliberately runs ahead of the golden template.
- Per-schedule time zones; overlap policies other than skip.
