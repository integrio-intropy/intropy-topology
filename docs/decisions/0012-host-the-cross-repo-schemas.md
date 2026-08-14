# 0012: The topology repo hosts the cross-repo contract schemas

> Status: implemented.

**Background.** Three repos cooperate to take an integration system from declaration to
deployment: `intropy-topology` declares and validates the system, `intropy-templates`
renders scaffolds and manifests, and `intropy-cli` orchestrates both. The architecture is
clean — topology mints, templates render, the CLI orchestrates — but the *conventions*
between them were duplicated facts kept in sync by comments and golden tests.

A cross-repo analysis (2026-08-14) enumerated the leaks: the block-kind string was minted
by a template label, re-declared as a Go parser registry, re-parsed case-insensitively in
manifest code, and consumed by the topology builders — four parties, one string, no shared
schema. The kebab→Pascal identifier derivation existed in three languages. The local-run
component set was generated twice. Two fixture catalogs had to stay identical by hand. And
the CLI's Go topology decoder had already drifted from the C# emitter: `GraphDocument`
emitted `services` and a `development` section the decoder never modeled — a live instance
of exactly the failure the boundary was supposed to prevent.

The root cause was ownership. Each convention had many declarers and no owner, so the
contract was whatever each consumer happened to expect. Drift was not caught at the emitter
— where it is cheap — but absorbed by the consumer, where it surfaces as a silent lie.

**Decision.** One repo owns a machine-checked schema for every cross-repo contract, and the
other two validate against a fetched copy rather than re-declaring it. This repo hosts the
schemas under `schemas/`:

- `topology-graph.schema.json` — the `topology.intropy.io/v1` record this repo emits.
- `scaffold-record.schema.json` — the `.intropy/scaffold.json` record, hosted here for
  one-stop fetching but **authored by the CLI** (`internal/template/scaffold.go`); this
  repo never reads or writes it.
- `block-kinds.schema.json` — the canonical spelling of every block kind.
- `port-bindings.schema.json` — the canonical spelling of every port binding / fixture kind.

Hosting them here is a deliberate application of 0010's ownership test. The graph record is
the one contract this repo *mints* (it is the single emission site, already versioned by
`apiVersion`), so its schema belongs beside the emitter. The others are hosted here purely
for one-stop fetching — a consumer pins one tag and gets every schema — not because this
repo owns their content; each schema's `$comment` names its real author so the ownership
test stays honest.

Two rules make the schemas enforceable rather than aspirational:

- **The schema describes the wire, not the type.** `topology-graph.schema.json` is written
  from what the `graph` verb actually prints, and the conformance test
  (`GraphDocumentSchemaConformance`) validates the verb's captured stdout against it — not
  the private `GraphDocument` record. A change that breaks the contract fails this repo's
  own build, at the emitter, before any consumer sees it.
- **Additive-only.** Every level allows additional properties, so a forward-compatible
  emitter that adds a field still validates against an older consumer's schema. Breaking
  changes (rename, retype, remove) require an `apiVersion` bump, which the CLI already
  fails loudly on. The schema can only tighten on a major bump.

**Consequences.** Consumers vendor the schemas at a pinned release and validate in their own
tests and CI; nothing fetches them at runtime. The block-kind and port-binding vocabularies
now have a single spelling, replacing the case-folding shim the CLI carried to paper over
emitter inconsistency. The scaffold-record schema is the one the topology repo hosts but
does not author — recorded here so a future reader does not mistake hosting for ownership.

What this does **not** do: it does not move the workload-shape policy (`extractor` →
CronJob) out of the CLI — that decision's owner is unresolved and is deferred until the kind
vocabulary it keys on has a single home, which this decision now provides. It also does not
unify the two local-run generators (C# for F5, template YAML for k3s); those stay parallel
implementations, now with a shared contract to point at.
