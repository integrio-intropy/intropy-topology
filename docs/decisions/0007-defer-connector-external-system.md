# 0007: Defer a connector's external system to system assembly

> Status: implemented.

**Background.** ADR 0002 redefined a connector as *the named connection point between the system
and an external system*, carrying two facts beyond its name: the **external system** it connects to
(*what*) and its **transport** (*how* — the Dapr binding `spec.type`). The transport was already made
optional — a connector could be declared *unresolved* (`Define(name, system)`), deferring the
transport to a later system-assembly step (`intropy system create`) and surfacing as a warning
(ITP107) rather than an error. The external system, however, was still required at declaration, which
is where a block scaffold (`intropy int create`) emits its connectors.

**Decision.** Defer the external system too. Only a connector's **name** is intrinsic to a component —
it is the local port the component reads from (`From`) or writes to (`To`). *What* it connects to and
*how* are both **resolution concerns, bound together at system assembly**, not at block scaffolding.

- `ConnectorRef.Define("erp")` — name only — is the form a block scaffold emits. Both external system
  and transport are deferred.
- `ConnectorRef.Define(name, system)` and `ConnectorRef.Define(name, system, transport)` remain, for
  the partially- and fully-resolved forms produced during/after assembly.
- `ConnectorRef.System` and `ConnectorResource.ExternalSystemName` are now nullable.

**Why this is the right split.** The external system was never load-bearing for topology validation.
Its only two touch-points were ITP104 (flagging *one connector name declared against two different
external systems* — a redundancy check that only existed because the name was duplicated across
declarations) and the materializer copying it into the output model. That is the same profile the
transport had before it was deferred. Splitting "what" (block time) from "how" (assembly time) was the
awkward part; putting both at `intropy system create` makes connector *resolution* one coherent step.
It also mirrors Dapr precisely: a block references `binding.<connector-name>` (derived from the name);
the external target and `spec.type` are component config resolved at deploy/assembly.

**When this would change.** If the topology ever needs external-system identity for cross-system
reasoning — a catalog view of "which systems talk to which external systems", or a rule that two
connectors must not target the same external system — it would still be *resolved at assembly and then
landed in the model*; it would not move back into the block scaffold.

## Mechanics

- **ITP104** (`ConnectorConflictRule`): the external-system half now compares only *resolved* (non-null)
  systems, exactly as the transport half already does. A name-only usage alongside a resolved one is
  deferral, not conflict.
- **ITP107** (`UnresolvedConnectorRule`): fires when the materialized connector is missing its external
  system *or* its transport, naming the missing facets. Warning severity, so a SystemHost still builds
  before the connector is resolved.
- **Materializer**: the connector accumulator upgrades each unresolved facet independently — a resolved
  usage anywhere resolves the connector (first-seen wins on identity; missing facets fill in).
- **Generation**: dev binding metadata is keyed by external system, so an unresolved connector simply
  has none to look up; the binding still emits with its transport-unresolved `TODO` marker.

The reference `OrderFulfillment.SystemHost` example declares fully resolved connectors because it
represents an already-assembled system; the deferral is a library capability exercised in tests,
exactly as the transport deferral is.
