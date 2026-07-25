# System-level platform services

> Status: accepted (2026-07-25). Re-adds, in a narrowed form, the platform-service
> concept that [ADR 0004](0004-edges-over-triggers.md)'s PoC lean pass removed "to be
> re-added when a concrete need appears".

## Context

Integration components depend on shared platform services — the first concrete case is a
shared idempotency store — that must be running before the components and their Dapr
sidecars start. The Aspire run backend hard-codes exactly one backend container
(RabbitMQ); a SystemHost declaration had no way to say "this system also needs service X",
so an F5 run raced against services the developer had to start by hand.

ADR 0004 preserved the removed design (`ServiceRef`, `UseService`, `.Uses`,
`ServiceResource`, `ServiceUseEdge`, `SystemTopology.Services`, ITP301/ITP402) for
cherry-picking. **The `full-topology` branch that CLAUDE.md points to for such re-adds
does not exist in this clone**, so this feature is re-implemented from the preserved
design rather than cherry-picked; CLAUDE.md is amended accordingly.

## Decision

- **Refs**: `ServiceRef.Define(name, Service.Container(image, port))`, declared as static
  fields in a scaffolded `Services.cs` — mirroring `ConnectorRef`/`Transport`, including
  the JSON-discriminated hierarchy (`$service`, one kind: `container`). The image string
  embeds an optional tag (the backend-neutral Docker reference form; Aspire's
  image/tag split is an Aspire-layer detail). Digest references are unsupported. Service
  names are DNS-1123 labels.
- **System-level, not per-component**: `SystemBuilder.UsesService(ref)` (returns `void` —
  `Add*` means "declare a block, get its builder back", which a service is not). There is
  no per-component `.Uses` edge and no `ServiceUseEdge`: every component waits for every
  declared service. Services are the one resource that does *not* materialize from usage —
  no edge references them, so they are declared explicitly.
- **Model**: `SystemTopology.Services : IReadOnlyList<ServiceResource>` (`Name`,
  `Service`), first-seen wins on duplicate names, sorted ordinally. Materialization never
  fails; conflicts are validation's to report.
- **No connectivity contract**: no env vars, no connection strings. Starting the service
  and the startup ordering is the whole contract — components reach the service on its
  fixed port, which the declaration states. The declared port is both the container port
  and the host port (the generate-before-run constraint that also fixes RabbitMQ's port).
- **Scope**: model + Aspire. Generated artifacts (Dapr YAML, `*.intropy.json`) are
  untouched; the `graph` verb lists services (presentation only, omitted when none).

## Diagnostics (code ledger changes)

- **ITP108 (new)**: one service name declared with two different definitions — the
  service twin of ITP104, in the resource-identity-conflict band (105–107 stay retired).
- **ITP402 (revived)**: a service name colliding with a component name, or the reserved
  `rabbitmq` broker name — its original ADR 0004 meaning; it was removed with its feature,
  never retired with prejudice. Services and components share one resource-name namespace
  when the system runs.
- **ITP301 (retired permanently)**: "service declared but unused" presupposed the
  per-component `.Uses` edge; with system-level services there is no usage to check.
- Deliberate non-rules: a services-only system still errors ITP002 (services declare no
  integration); ITP302 is unchanged (a service is not a component edge).

## Run-backend semantics

Each declared service becomes a plain `AddContainer` (never an Aspire integration
package — same reasoning as RabbitMQ in [ADR 0005](0005-aspire-run-backend.md): generated
configuration speaks fixed, known endpoints) appended to the existing backend machinery:

- every component and every Dapr sidecar `WaitFor`s it;
- the pre-start gate holds every `*-dapr-cli` sidecar until its TCP probe answers;
- sidecar recovery treats it as a backend whose readiness gates restart attempts.

## Known limits / out of scope

- **Port clashes are not validated**: two services on one port (or a service on 5672)
  fail at container bind time with a clear Docker/DCP error. A future ITP rule could
  catch this at `Build()`.
- **A component named `rabbitmq`** collides with the broker resource exactly like a
  service would; the pre-existing gap for components is noted here, not fixed (ITP402
  covers services only).
- Digest image references; a container whose internal port differs from the host port.
- `intropy sys create` codegen emitting `Services.cs`/`UsesService` (intropy-cli repo) —
  until then the example host runs ahead of the golden template, the ADR 0010 precedent.
- Kubernetes generation for services (no workload generation exists at all yet).
