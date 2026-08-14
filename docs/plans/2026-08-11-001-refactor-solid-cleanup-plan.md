---
title: Refactor SOLID violations and code smells
type: refactor
status: active
date: 2026-08-11
---

# Refactor SOLID violations and code smells

## Overview

Apply the findings from the 2026-08-11 static analysis of the three packages
(`Intropy.Topology`, `Intropy.Topology.Generation`, `Intropy.Topology.Aspire`): remove
duplicated logic, fix a layering violation where the "dependency-free" packages bake in
local-runtime constants, consolidate error handling, and separate responsibilities that
currently sit in single oversized units. Every unit lands behavior-preserving for the
public DSL surface except where explicitly called out as a deliberate contract change
(all changes are pre-launch, so backwards compatibility is not a constraint).

---

## Problem Frame

The analysis found ten findings plus minor observations. The load-bearing ones:

- Duplicated algorithms (path containment, single-implementation discovery, unresolved-
  connector validation) that will drift as the `full-topology` merge grows the codebase.
- Local-runtime facts (Microcks URL/port 8585, the `binding.<name>` derivation, the
  `default` port literal) replicated across package boundaries, with the Generation
  package owning an Aspire-side deployment concern (`OpenApiMock.BaseUri`).
- Inconsistent exception policy: `DevelopmentValidationException` inherits
  `InvalidOperationException` only to widen catch filters, and CLI verb handlers
  catch bare `InvalidOperationException`, which swallows programming errors and
  reports them as user input errors.
- Validation rules read two different sources of truth (`SystemBuilder` internals vs.
  the materialized model) with no stated boundary, coupling rules to the materializer's
  dedup behavior.
- `IntropyAspire.Apply` mixes backend setup, event gating, component wiring, and startup
  ordering in one method; the subscriber-first ordering graph algorithms
  (`SubscriberPrecedence`, `HasCycle` — already pure, but `private static`) are
  unreachable for direct unit testing.
- `ComponentOverlay.Parse` is a line-oriented YAML state machine with no documented
  grammar contract — the most bug-prone code in the repo.

---

## Requirements Trace

- R1. One owner per fact: Microcks endpoint, `binding.` derivation, `default` port, path
  containment, single-definition discovery, unresolved-connector validation.
- R2. The dependency-free packages carry no local-runtime deployment constants beyond the
  one the `graph` JSON contract forces on them (`baseUri`, see U2); the Aspire package
  composes runtime URLs from its own port constants. R2 is intentionally only partially
  satisfiable — the contract pins the rest.
- R3. User input errors and programming errors are distinguishable: user errors become
  focused validation exceptions with exit code 1; invariant violations crash loudly.
- R4. Every validation rule has a single, documented source of truth.
- R5. Startup ordering (subscriber precedence, cycle detection) is unit-testable without
  DCP or the Aspire hosting model.
- R6. No public DSL behavioral change: `SystemBuilder`, block builders, refs,
  `SystemTopology`, the `graph` JSON contract, and generated YAML stay identical unless a
  unit explicitly declares a change, and every change is covered by updated tests and the
  JSON snapshot.

---

## Scope Boundaries

- No new features or model changes (named ports, new component kinds, transports).
- No dependency additions (e.g., no real YAML parser) — the dependency-free core invariant
  stands; the YAML parser gets a documented contract, not a replacement.
- No changes to `DaprSidecarRecovery`'s state-classification logic (analysis flagged it as
  deliberately good).
- Examples (`examples/`) are updated only where a deliberate contract change forces it.

### Deferred to Follow-Up Work

- Kind-dispatch switches in `CompletenessRules` (finding #5): tolerable at two cases and
  entangled with the `full-topology` branch merge — revisit when the full model lands.
- CLI codegen templates in `intropy-cli` that mirror scaffolded files: sync only if a unit
  here changes the golden-output shape (none currently do).

---

## Context & Research

### Relevant Code and Patterns

- `src/Intropy.Topology.Generation/Development.cs` — `FileRootPath.Inspect` and
  `OpenApiArtifact.Inspect` carry the duplicated containment algorithm (R1).
- `src/Intropy.Topology/Discovery.cs` — `SystemDiscovery.FindSingleDefinition` is the
  canonical scan-and-instantiate pattern to generalize.
- `test/Intropy.Topology.Test/Building/EndToEndTests.cs` — byte-exact JSON snapshot; the
  contract guard for any model-adjacent change.
- `docs/plans/2026-07-19-graph-development-flag.md` — the graph contract includes
  `development.mocks[].baseUri`; units touching it must keep the serialized shape.
- Aspire package seams (`IResourceLifecycle`, `IResourceStateMonitor`, `IBackendReadiness`)
  — the established pattern for testability; new seams should mirror it.

### Institutional Learnings

- CLAUDE.md invariants: per-block builders = compile-time legality; materialization never
  fails; flat `Intropy.Topology` is the user-facing surface; file layout is grouped, not
  one-type-per-file — new types go in the existing file they belong with.

---

## Key Technical Decisions

- **Introduce an `IntropyException` marker base** for user-facing validation errors
  (`TopologyValidationException`, `DevelopmentValidationException`) and catch that in CLI
  entry points, instead of catching `InvalidOperationException`. Rationale: breaks the
  inheritance from a framework exception that exists only to widen filters (R3).
- **Microcks base URL moves to the Aspire package** as a single composed constant;
  `OpenApiMock` carries only identity (appId, artifact path, title, version). The
  Generation package receives the base URL as a parameter where it needs one. The
  `graph` JSON keeps emitting `baseUri` (contract preservation, R6) by accepting the URL
  from a well-known internal constant — flagged in Open Questions because it sits on the
  R2/R6 boundary.
- **Validation rules split by source of truth**: rules needing declaration-level detail
  (duplicates destroyed by materializer dedup) keep builder access via a narrowed,
  explicitly named surface; model-level rules see only `SystemTopology`.
- **Startup ordering becomes a pure graph module** (`StartupOrdering`) consumed by
  `IntropyAspire.Apply`, unit-tested directly.

---

## Open Questions

### Resolved During Planning

- Should `OpenApiMock.BaseUri` be deleted outright? **No** — the `graph` JSON contract
  (`topology.intropy.io/v1`) includes `baseUri`, and that contract is consumed
  externally. Keep the property shape in the serialized document; change who owns the
  constant.
- Replace the hand-rolled YAML parser? **No** — dependency budget forbids it; document
  the accepted subset and add tests pinning it.

### Deferred to Implementation

- Exact names of new helpers (`PathContainment`, `SingleImplementation`, `StartupOrdering`)
  — decide against the grouped-file convention when writing them.
- Whether `StartupOrdering` lives in the Aspire package or the Generation package —
  lean Aspire (its only consumer); decide when extracting.

---

## Implementation Units

- [ ] U1. **Shared path-containment helper in Generation**

**Goal:** Collapse the four copies of the root-escape / symlink-resolution algorithm
(finding #1) into one helper.

**Requirements:** R1

**Dependencies:** None

**Files:**
- Modify: `src/Intropy.Topology.Generation/Development.cs` (`FileRootPath`,
  `OpenApiArtifact`, new internal helper)
- Test: `test/Intropy.Topology.Generation.Test/DevelopmentTests.cs`

**Approach:**
- Add an internal static helper (grouped in `Development.cs` or its own file if the
  file grows past comfort) exposing: resolve+canonicalize root (with symlink target),
  combine+canonicalize a relative path, and assert containment, producing
  `DevelopmentValidationException` with the caller-supplied context on failure.
- Rewrite `FileRootPath.Inspect` and `OpenApiArtifact.Inspect` to use it, preserving the
  exact current exception messages (tests assert on them).

**Patterns to follow:**
- Existing `DevelopmentValidationException` message style in `Development.cs`.

**Test scenarios:**
- Happy path: relative path inside root resolves and passes containment.
- Edge case: path equal to the root itself passes (current `string.Equals` branch).
- Error path: `../` escape throws `DevelopmentValidationException` with the same message
  as today (the only case current tests assert on — `DevelopmentTests.cs`).
- Error path: symlinked directory inside root pointing outside throws — **message differs
  per caller today**: only `OpenApiArtifact.Inspect` produces the "escapes … through a
  symlink" variant; `FileRootPath.Inspect` resolves the directory first and emits the
  plain variant. Decide during implementation whether the helper unifies the message
  (preferred: yes, document the unification) or keeps both; update tests accordingly.
- Error path: `IOException` during link resolution surfaces as
  `DevelopmentValidationException` with the underlying message.

**Verification:**
- `dotnet test --filter FullyQualifiedName~DevelopmentTests` passes with no message changes.

---

- [ ] U2. **Microcks endpoint ownership moves to Aspire**

**Goal:** Remove the hardcoded `http://localhost:8585` from `MicrocksImporter` and the
ad-hoc URL composition on `OpenApiMock` (finding #2); one owner for the URL composition,
one for the port. Note: the `graph` JSON contract requires `baseUri`, so the composed
origin necessarily stays *known* to the Generation package — this unit consolidates and
documents that knowledge rather than eliminating it (R2 partial, by design).

**Requirements:** R1, R2

**Dependencies:** None

**Files:**
- Modify: `src/Intropy.Topology.Generation/Development.cs` (`OpenApiMock`)
- Modify: `src/Intropy.Topology.Generation/Generation.cs` (`TopologyGenerator.Generate`,
  `HttpEndpoint`)
- Modify: `src/Intropy.Topology.Generation/IntropyGenerate.cs` (`GraphMock` mapping)
- Modify: `src/Intropy.Topology.Aspire/IntropyAspire.cs` (`MicrocksPort`)
- Modify: `src/Intropy.Topology.Aspire/Microcks.cs` (`MicrocksImporter`)
- Test: `test/Intropy.Topology.Generation.Test/TopologyGeneratorTests.cs`,
  `test/Intropy.Topology.Generation.Test/IntropyGenerateTests.cs`

**Approach:**
- `OpenApiMock` drops `BaseUri`; it keeps `AppId`, `ArtifactPath`, `Title`, `Version`.
- Add one internal constant holder in Generation for the local mock base origin
  (documented as *local-runtime convention shared with the Aspire backend* — the graph
  contract forces Generation to know it; see Open Questions) plus a method composing the
  REST path from title/version (the escaping rules stay with the mock identity).
- `TopologyGenerator.Generate` and the `graph` mapper use that single composition point.
- Aspire: `MicrocksImporter` builds its base address from `IntropyAspire.MicrocksPort`
  instead of the literal.

**Test scenarios:**
- Happy path: generated HTTPEndpoint YAML contains the composed base URL with escaped
  title/version segments (existing generator tests updated).
- Happy path: `graph --development` JSON still contains `baseUri` with the identical
  value as before (existing graph tests updated).
- Edge case: title containing spaces/slashes escapes as individual path segments
  (existing behavior, pinned by test).

**Verification:**
- Full Generation test suite passes; no literal `8585` or `localhost` remains in
  Generation source outside the single composition point.

---

- [ ] U3. **Single owner for derived names (`binding.` prefix, `default` port)**

**Goal:** Delete inline `$"binding.{name}"` and the `"default"` literal outside their
owning declarations (finding #2).

**Requirements:** R1

**Dependencies:** None

**Files:**
- Modify: `src/Intropy.Topology/Components.cs` (visibility of `DefaultPort`)
- Modify: `src/Intropy.Topology/Model/Resources.cs` (shared derivation helper, if not
  placed on the model type itself)
- Modify: `src/Intropy.Topology.Generation/Generation.cs` (`ComponentConfig`)
- Modify: `src/Intropy.Topology.Generation/IntropyGenerate.cs` (`GraphComponent.From`)
- Test: `test/Intropy.Topology.Generation.Test/IntropyGenerateTests.cs`,
  `test/Intropy.Topology.Test/Building/EndToEndTests.cs`

**Approach:**
- `ComponentConfig` builds its `DaprComponent` values from the owning derivation. Note:
  `ComponentConfig` iterates `ComponentModel.Connectors` (`ConnectorEdge`), which has no
  `ConnectorResource` reference, so reusing the computed property requires passing the
  topology's connector resources (or their names) into `ComponentConfig` and looking up
  by name — or exposing the derivation as a shared internal helper both call sites use.
  Pick the helper: it avoids inventing a lookup that does not exist today.
- Make `Component.DefaultPort` visible to Generation. Verified: no
  `InternalsVisibleTo("Intropy.Topology.Generation")` exists today
  (`src/Intropy.Topology/AssemblyInfo.cs` only names the test assembly) — so either add
  that attribute (files list updated) or make it `public const`, which is defensible
  since the value is part of the serialized model semantics. Prefer `public const` to
  avoid widening the internal seam for one constant.
- Use it in `GraphComponent.From` (the omit-default comparison) instead of `"default"`.

**Test scenarios:**
- Happy path: `graph` output omits `port` when it equals the default and includes it
  otherwise (existing coverage continues to pass).
- Happy path: component config JSON `daprComponent` values unchanged (byte-exact
  comparison against current generator output).

**Verification:**
- Generator and graph tests pass; `grep -n '"binding\.\|"default"' src/` shows hits only
  at the owning declarations and comments.

---

- [ ] U4. **Exception taxonomy and CLI catch policy**

**Goal:** Introduce a marker base for user errors; stop catching
`InvalidOperationException` (finding #3); align `Graph`'s unfiltered catch with its
siblings.

**Requirements:** R3

**Dependencies:** None

**Files:**
- Modify: `src/Intropy.Topology/TopologyValidationException.cs` (new public base, or a
  new small file if the grouped-file convention prefers it)
- Modify: `src/Intropy.Topology.Generation/Development.cs`
  (`DevelopmentValidationException` re-parented)
- Modify: `src/Intropy.Topology.Generation/IntropyGenerate.cs` (catch filters in
  `Check`, `Graph`, `Generate`; `RunAsync` made synchronous)
- Modify: `src/Intropy.Topology.Aspire/IntropyAspire.cs` (`RunAsync` catch filter)
- Modify: `examples/OrderFlow.SystemHost/Program.cs` (call-site update)
- Test: `test/Intropy.Topology.Generation.Test/IntropyGenerateTests.cs` (**full rewrite
  of call shape** — every test currently awaits `RunAsync`)

**Approach:**
- New public abstract `IntropyException : Exception` in the core package; both
  validation exceptions derive from it. `DevelopmentValidationException` no longer
  derives from `InvalidOperationException` — deliberate contract change, pre-launch
  (callers catching `InvalidOperationException` around development discovery must catch
  `IntropyException`; update example host and tests).
- CLI handlers catch `IntropyException` only. Invariant violations (e.g.
  `Single`-on-missing in the generator) propagate as crashes — that is the intent.
- `Graph` gets the same filtered catch; `Unknown` keeps exit code 2.
- `IntropyGenerate.RunAsync` becomes a synchronous `Run` returning `int` (no async
  work exists in its body). This is a test-surface rewrite, not a tweak: all ~10 tests
  in `IntropyGenerateTests.cs` currently `await IntropyGenerate.RunAsync(...)` through an
  async capture helper — the whole file's call shape changes. Production call sites:
  `examples/OrderFlow.SystemHost/Program.cs` (single ternary switching on
  `IntropyAspire.RunAsync` vs `IntropyGenerate.RunAsync`; after the rename the branches
  are stylistically asymmetric — acceptable, the Aspire backend stays async).
  Alternatives considered: keep the `RunAsync` name on a synchronous body (rejected:
  the name would lie), or leave the API untouched and drop this minor observation
  (acceptable fallback if the rewrite proves noisy).

**Test scenarios:**
- Happy path: valid system via `check` exits 0.
- Error path: invalid topology via `check` exits 1 with the full diagnostic message on
  stderr.
- Error path: invalid development definition via `generate` exits 1 (was: same — pinned).
- Error path: a programming error (simulated invariant violation, e.g. via a malformed
  in-memory topology passed to `TopologyGenerator.Generate` directly) throws rather than
  being converted to exit code 1.
- Edge case: unknown verb exits 2.

**Verification:**
- Verb tests pass; no catch filter in entry points names `InvalidOperationException`.

---

- [ ] U5. **One validation boundary for unresolved connectors**

**Goal:** `DevelopmentBuilder.Build` is the single validator; `TopologyGenerator.Generate`
trusts the manifest (finding #10).

**Requirements:** R1

**Dependencies:** U4 (ordering rationale, stated precisely: today the generator's
`InvalidOperationException` *is* caught by the CLI filters and reported as a user
error; after U4 those filters no longer catch it, so replacing `SingleOrDefault`+throw
with `Single` — same exception type — makes an unreachable invariant violation crash
loudly instead. The throw is not removed; its catchability is.)

**Files:**
- Modify: `src/Intropy.Topology.Generation/Generation.cs` (drop the per-connector
  `SingleOrDefault` + throw; use `Single`)
- Test: `test/Intropy.Topology.Generation.Test/DevelopmentTests.cs`,
  `test/Intropy.Topology.Generation.Test/TopologyGeneratorTests.cs`

**Approach:**
- Keep the manifest-time check (focused `DevelopmentValidationException` listing all
  unresolved connectors) — it reports the whole picture at once, matching the Build()
  collects-everything philosophy.
- The generator indexes the manifest by connector name into a dictionary once and looks
  up with a hard invariant (`Single` semantics), no user-facing throw.

**Test scenarios:**
- Error path: manifest built for a topology with an unresolved connector throws
  `DevelopmentValidationException` naming every unresolved connector (existing
  coverage).
- Integration: `generate` verb on such a system exits 1 with the manifest message, not a
  generator crash (covered end-to-end through the verb).
- Edge case: manifest with zero connectors and a zero-connector topology generates fine.

**Verification:**
- Generator contains no `SingleOrDefault … is null → throw` branch; verb tests pass.

---

- [ ] U6. **Split validation rules by source of truth**

**Goal:** Every rule reads exactly one source; the materializer's dedup-destroyed
information travels through an explicit channel (finding #4, R4).

**Requirements:** R4

**Dependencies:** None

**Files:**
- Modify: `src/Intropy.Topology/Validation/ITopologyRule.cs` (context split)
- Modify: `src/Intropy.Topology/Validation/TopologyRules.cs`
- Modify: `src/Intropy.Topology/Validation/Rules/TopicAndConnectorRules.cs`,
  `src/Intropy.Topology/Validation/Rules/CompletenessRules.cs`,
  `src/Intropy.Topology/Validation/Rules/ServiceRules.cs`,
  `src/Intropy.Topology/Validation/Rules/StructureRules.cs`,
  `src/Intropy.Topology/Validation/Rules/UsageAndCollisionRules.cs`
- Test: `test/Intropy.Topology.Test/Validation/RuleTests.cs`,
  `test/Intropy.Topology.Test/Validation/BuildValidationTests.cs`

**Approach:**
- Split `ValidationContext` into two views: a declaration view (builder components with
  raw call lists — internal) and a model view (`SystemTopology`).
- Two rule interfaces (or one interface with two overload-shaped contexts — pick the
  shape that keeps `TopologyRules.Run` a flat ordered list): declaration rules
  (`DuplicatePortRule`, `DuplicateSubscriptionRule`, `TopicContractConflictRule`,
  `MissingRequiredOutputRule`, `MissingRequiredSubscriptionRule`, the per-component half
  of `ServiceUsageRule`) and model rules (everything else, plus the collision half of
  `ServiceUsageRule` which reads both — split that rule in two).
- Document on the interfaces: declaration rules inspect raw declarations; model rules
  must not touch builder state. Rationale, stated accurately (review correction): the
  materializer dedups **only** services and connector edges (`seenServices` /
  `seenConnectorEdges`); `Subscribes`/`Publishes` append every call, so
  `DuplicatePortRule` and `DuplicateSubscriptionRule` could equally read the model.
  The declaration view exists because *some* rules need it (service duplicates,
  contract conflicts via `TopicRef.ContractType` which the model erases to a string),
  not because all six do — the split's real contract is "rules that need erased or
  deduped declaration detail" vs "rules over the materialized picture". During
  implementation, prefer moving `DuplicatePortRule`/`DuplicateSubscriptionRule` to the
  model side if the model already carries what they need; the unit's goal is one source
  per rule, not a predetermined side for each rule.

**Test scenarios:**
- Happy path: the full existing rule test matrix passes unchanged (diagnostic messages,
  severities, targets byte-identical).
- Integration: `BuildValidationTests` (rules composing through `Build()`) unchanged and
  green.
- Edge case: a component with two `Uses` of the same service and a service/appId
  collision reports both diagnostics (the split `ServiceUsageRule` halves still compose).

**Verification:**
- Rule tests and build-validation tests pass with zero message changes; no model rule
  references `SystemBuilder`.

---

- [ ] U7. **Extract `StartupOrdering` and slim `IntropyAspire.Apply`**

**Goal:** Move `SubscriberPrecedence`/`HasCycle` into a pure, directly testable module;
break `Apply` into named collaborators (finding #7, R5).

**Requirements:** R5

**Dependencies:** None

**Files:**
- Modify: `src/Intropy.Topology.Aspire/IntropyAspire.cs`
- Create: `src/Intropy.Topology.Aspire/StartupOrdering.cs` (or grouped into an existing
  file if small — default: new file, it is a cohesive unit)
- Test: `test/Intropy.Topology.Aspire.Test/IntropyAspireTests.cs` (existing),
  new `test/Intropy.Topology.Aspire.Test/StartupOrderingTests.cs`

**Approach:**
- `StartupOrdering` exposes: build precedence edges from `SystemTopology`, detect cycle,
  and yield publisher→subscribers pairs — pure functions over the model.
- `Apply` decomposition: extract Redis backend setup, Microcks backend setup, and the
  `BeforeResourceStartedEvent` gating handler into private methods (or small internal
  types mirroring the existing seam style) so `Apply` reads as: backends → gate →
  components → ordering.
- No behavior change to the gating logic itself; the `sidecarWaits` closure capture is
  preserved.

**Test scenarios:**
- Happy path: publisher/subscriber pair yields one precedence edge (pure unit test).
- Edge case: self-loop (component publishes and subscribes the same topic) yields no edge.
- Edge case: cycle (A→B, B→A) reports a cycle; acyclic graph reports none.
- Edge case: publisher with no subscribers yields no entry.
- Integration: existing `IntropyAspireTests` (model-inspection tests through `Apply`)
  pass unchanged, proving the decomposition preserved wiring.
- Integration (**new**): the cycle branch in `Apply` — a cyclic topology produces the
  stderr warning and skips ordering. No existing test covers that path, and the
  decomposition could silently drop the message; pin it.

**Verification:**
- New `StartupOrderingTests` green; `Apply` body no longer contains the graph algorithms;
  existing Aspire tests unchanged and green.

---

- [ ] U8. **Generalize single-implementation discovery**

**Goal:** One scan-find-instantiate helper for `ISystemDefinition` and
`IDevelopmentDefinition` (finding #9).

**Requirements:** R1

**Dependencies:** None (standalone; may land in parallel with U4/U6)

**Files:**
- Modify: `src/Intropy.Topology/Discovery.cs`
- Modify: `src/Intropy.Topology/AssemblyInfo.cs` — **add**
  `[assembly: InternalsVisibleTo("Intropy.Topology.Generation")]` (review-verified gap:
  only the test assembly is named today; the seam is *not* established between src
  packages)
- Modify: `src/Intropy.Topology.Generation/Development.cs` (`DevelopmentDiscovery`)
- Test: `test/Intropy.Topology.Test/DiscoveryTests.cs` (four existing facts are the
  message-preservation guard),
  `test/Intropy.Topology.Generation.Test/DevelopmentTests.cs`

**Approach:**
- Core package gains an internal generic helper: given types, an arity rule
  (exactly-one / zero-or-one), and a naming function for error messages, return the
  instantiated candidate(s) or report the arity failure. Return an arity result
  (enum/discriminated result) rather than throwing, so error types stay package-local:
  core's `SystemDiscovery` maps failure to `InvalidOperationException`; Generation's
  `DevelopmentDiscovery` maps it to `DevelopmentValidationException`.
- Note the test fixtures that also exercise discovery: `test/Intropy.Topology.WipSystem/`
  and `test/Intropy.Topology.Generation.Test/TestSystem.cs` carry exactly-one-definition
  assemblies end-to-end; they must keep passing unchanged.
- The new `InternalsVisibleTo` widens the src-package internal seam; acceptable (single
  helper, same solution), and it is the same seam U3 evaluated and rejected for one
  constant — here it earns its place.

**Test scenarios:**
- Happy path: exactly one definition discovered and instantiated.
- Error path: zero definitions — core throws its current message; dev discovery returns
  an empty manifest (arity zero-or-one).
- Error path: multiple definitions — both callers produce their current, package-specific
  messages listing candidate type names.

**Verification:**
- The four existing `DiscoveryTests` facts and dev-discovery tests pass with identical
  error messages; WipSystem-based fixtures still build and discover.

---

- [ ] U9. **Document the `ComponentOverlay` YAML subset contract**

**Goal:** Pin the accepted YAML grammar in a comment and lock it with tests (finding #8).

**Requirements:** R6

**Dependencies:** None

**Files:**
- Modify: `src/Intropy.Topology.Aspire/ComponentOverlay.cs` (contract comment only)
- Test: `test/Intropy.Topology.Aspire.Test/ComponentOverlayTests.cs`

**Approach:**
- Write the subset contract as a doc comment on `Parse`: block style only, no anchors/
  aliases/flow collections, single document (multi-doc already rejected), top-level keys
  at column 0, `metadata.name` and `scopes` recognized per the stated rules.
- Add tests that pin both accepted shapes and known-rejected ones, so future drift in
  the parser is a test failure, not a silent staging change.

**Test scenarios:**
- Happy path: canonical generator-shaped YAML parses name/kind/scopes (existing).
- Edge case: quoted scalar values with embedded quotes parse per the `Trim('"','\'')` rule.
- Error path: multi-document YAML throws (**new test** — no existing test asserts the
  multi-doc rejection in `ComponentOverlay.Parse`).
- Edge case: a file with `name:` under a nested non-metadata block does not get mistaken
  for `metadata.name` — pin current behavior (whatever it is) with a test named for the
  contract, and note the limitation in the comment.
- Edge case: CRLF line endings parse identically to LF.

**Verification:**
- New tests green; the doc comment states the subset in the same style as existing
  invariant comments.

---

- [ ] U10. **Builder-base cleanup and remaining minors**

**Goal:** Resolve the dead `TSelf` parameter, hoist duplicated `Uses`, and sweep the
remaining minor observations (findings #6, #10-minors).

**Requirements:** R1

**Dependencies:** None

**Files:**
- Modify: `src/Intropy.Topology/Building/ComponentBuilders.cs`
- Modify: `src/Intropy.Topology.Generation/Development.cs` (`MockBuilder.FromOpenApi`,
  `FileBuilder.RootPath` return type)
- Modify: `src/Intropy.Topology.Aspire/Microcks.cs` (`CountMatchingServices` hardening)
- Modify: `src/Intropy.Topology.Generation/IntropyGenerate.cs` (arg parsing: reject
  unrecognized non-flag arguments instead of ignoring)
- Test: `test/Intropy.Topology.Test/Building/BuilderMappingTests.cs`,
  `test/Intropy.Topology.Generation.Test/DevelopmentTests.cs`,
  `test/Intropy.Topology.Generation.Test/IntropyGenerateTests.cs`

**Approach:**
- Hoist `Uses(ServiceRef)` into `ComponentBuilder<TSelf, TComponent>` returning `TSelf`
  via a `private protected abstract` self-reference — this gives the CRTP parameter its
  purpose; remove the three duplicates.
- `MockBuilder.FromOpenApi` / `FileBuilder.RootPath` return the builder for chain
  consistency — deliberate, additive API change (existing void call sites still compile).
- `CountMatchingServices`: `GetProperty` on `name`/`version`/`type` becomes
  `TryGetProperty`-based; malformed entries throw `InvalidOperationException` with a
  message naming the Microcks services endpoint.
- Arg parsing: `generate` rejects unexpected positional arguments with the same
  unknown-command style message and exit code 2.

**Test scenarios:**
- Happy path: fluent `Uses` chains on all three builder kinds (existing tests compile and
  pass; add one mixed-chain assertion if absent).
- Edge case: Microcks services entry missing `type` produces the new focused error.
- Error path: `generate ./out extra-arg` exits 2 with an unrecognized-argument message.
- Happy path: `MockBuilder.FromOpenApi` chainable (compile-time proof suffices; no new
  runtime test needed).

**Verification:**
- All touched test suites pass; no builder kind lost a member (public API surface check
  via existing end-to-end tests).

---

## System-Wide Impact

- **Interaction graph:** U4 changes the base type of two public exceptions and one CLI
  entry signature (`RunAsync` → `Run`); example `Program.cs` files and any external
  SystemHost catch blocks are the affected call sites.
- **Error propagation:** after U4/U5, user errors flow exclusively as `IntropyException`
  subtypes to verb handlers; everything else crashes. This is the intended new contract.
- **State lifecycle risks:** none — all units are behavior-preserving transformations of
  pure code paths, except the deliberate contract changes listed per unit.
- **API surface parity:** the CLI golden output in `intropy-cli` mirrors scaffolded
  `Program.cs`; if U4 changes the entry-point call shape, the CLI template must be
  updated in the same release (tracked in intropy-cli, see Deferred).
- **Integration coverage:** `EndToEndTests`' byte-exact JSON snapshot guards U3/U6
  against model drift; Aspire model-inspection tests guard U7.
- **Unchanged invariants:** materialization never fails; per-block builders remain the
  only edge-legality enforcement; the `graph` JSON shape is byte-identical after U2/U3.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| U4's exception re-parenting breaks external catch blocks | Pre-launch; call out in PR description; grep examples/tests for `InvalidOperationException` catches |
| U2's graph `baseUri` handling erodes the dependency-free claim | Keep exactly one documented composition point; comment names the contract constraint |
| U6 rule split subtly reorders diagnostics | Rules array order preserved; snapshot and RuleTests pin exact output |
| U7 decomposition changes Aspire wiring order | Existing model-inspection tests cover `Apply` output; no timing changes to event subscription order |
| Units land in different orders than planned | All units are independent except U5→U4; merge conflicts likely only in `Development.cs` (U1, U4, U8, U10) — sequence those |

---

## Sources & References

- **Origin analysis:** chat session 2026-08-11 (SOLID/code-smell analysis of all three packages)
- Related code: `src/Intropy.Topology/`, `src/Intropy.Topology.Generation/`, `src/Intropy.Topology.Aspire/`
- Related plans: `docs/plans/2026-07-19-graph-development-flag.md` (graph contract shape)
