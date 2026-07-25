# Refactoring task: staged per-block builders

> Status: implemented

> **Scope note:** a follow-up task (`CONNECTOR-REDEFINITION.md`) will replace `BindingRef`
> with `ConnectorRef` afterwards. Do **not** anticipate it here — keep `BindingRef`,
> `FromBinding`, `ToBinding`, and `Trigger.Binding` names as-is in this task.

**Goal:** replace the single generic `ComponentBuilder` with staged (type-state) per-block
builders so that illegal topology declarations become compile errors instead of Build-time
diagnostics. Shrink the validation rule engine to only what types cannot check. This restores
the agreed design ("illegal topology = compile error, not validate error") and *reduces* total
code — if the diff grows the library, something went wrong.

**Do not change:** `TopologyMaterializer` semantics (never-fails, first-seen-wins, deterministic
ordering), the `SystemTopology` model shape (except the §7 rename),
`TopologyDiagnostic`/severity/ITP codes, `Build`/`TryBuild`/`Validate` on `SystemBuilder`,
XML doc style, zero external dependencies.

---

## 1. Target builder API

Each `Add*` returns a block-specific builder exposing **only that block's legal grammar**.
A method that would be illegal for the block type must not exist on its builder type.

```csharp
// Extractor: edge block, pulled/pushed from outside, exactly one output
s.AddExtractor("pim-extractor")            // ExtractorBuilder
    .TriggeredBy(Trigger.Schedule("*/15 * * * *"))   // Schedule | Http | Binding — NOT Topic
    .FromBinding(Bindings.Pim)             // optional, repeatable
    .Publishes(ProductFlow.Raw)            // default port; Publishes(port, ref) also allowed
    .Uses(idempotency);                    // optional, repeatable
// ExtractorBuilder has no ToBinding, no TriggeredBy(TopicRef...)

// Loader: consumes exactly one topic, writes to a binding, publishes nothing
s.AddLoader("erp-loader")                  // LoaderBuilder
    .TriggeredBy(ProductFlow.Enriched)     // exactly one TopicRef (sugar for Trigger.Topic)
    .ToBinding(Bindings.Erp);
// LoaderBuilder has no Publishes, no FromBinding, no Schedule/Http/Binding trigger overloads

// Assembler / Reconciler: core blocks, topic-triggered only, 1..n in, 1..n out (ports)
s.AddAssembler("product-assembler")        // AggregatorBuilder (shared by both)
    .TriggeredBy(ProductFlow.Raw, PriceFlow.Raw)     // params TopicRef[], at least one
    .Publishes("products", ProductFlow.Enriched)
    .Uses(idempotency);
// AggregatorBuilder has no FromBinding/ToBinding, no Schedule/Http/Binding trigger overloads

// Transactional Integration: edge-triggered, binding in and/or out, no topic publish (v1)
s.AddTransactionalIntegration("order-import")   // TransactionalIntegrationBuilder
    .TriggeredBy(Trigger.Http())
    .FromBinding(Bindings.Shop)
    .ToBinding(Bindings.Erp);

// Unchanged: AddContainerService(name, image), UseService(name)
```

Implementation notes:

- Enforce trigger-before-the-rest by stage: `AddExtractor(name)` returns a type whose only
  members are the legal `TriggeredBy` overloads; that call returns the full block builder.
  This kills the "multiple TriggeredBy calls" case entirely — after the first call the
  method no longer exists on the returned type.
- Share plumbing via an internal base class or an internal composed `ComponentCore`; the
  public surface is only the staged types. Prefer composition if inheritance forces public
  members onto the wrong stages.
- Components register with `SystemBuilder` at the `Add*` call (as today), so an abandoned
  chain is still visible to Build-time completeness rules (see §2).
- Type-state prevents *illegal* calls; it cannot prevent *missing* calls. Completeness stays
  a Build-time rule.

## 2. Validation rules: delete / keep

**Delete (the type system now enforces these):**

- `MultipleTriggerCallsRule` (ITP203) — unrepresentable after staging.
- `TriggerKindAllowedRule` (ITP204) — per-block `TriggeredBy` overloads.
- `BindingDirectionAllowedRule` — only legal binding methods exist per builder type.

Also delete the machinery that existed only for them: `ComponentBuilder._triggerCalls` as
`List<IReadOnlyList<Trigger>>` collapses to a single optional trigger call;
`TopologyMaterializer.EffectiveTriggerCall` simplifies accordingly.

**Keep (types cannot check these):**

- Completeness: `MissingTriggerRule` (abandoned chains), and add the analogous
  missing-required-output checks per block if not already covered (extractor without
  `Publishes`, loader without `ToBinding`).
- All cross-component rules: duplicate workload names, duplicate ports, duplicate trigger
  topics, topic contract conflicts, binding external-system conflicts, pubsub/binding and
  service/workload name collisions, unused service, empty system, no-edges.
- `CronExpressionRule` (see §4).

Retire the deleted ITP codes — do not reuse the numbers. Note them as retired in a comment in
`TopologyRules`.

**Test changes:** delete tests for the deleted rules; where practical replace with a comment
in the corresponding builder test noting the case is now a compile error. Do NOT build a
Roslyn compile-fail test harness for this — not worth the weight. Update
`BuilderMappingTests`/`EndToEndTests` to the staged API. All remaining tests must pass.

## 3. Fix the member-hiding smell in `Trigger`

`TopicTrigger.Topic` and `BindingTrigger.Binding` currently need `new` because they collide
with the static factories `Trigger.Topic(...)` / `Trigger.Binding(...)`. Rename the
**properties** (keep factory names — they're the public DSL surface and match the plan):
`TopicTrigger.Topic` → `TopicTrigger.SubscribedTopic`, `BindingTrigger.Binding` →
`BindingTrigger.InputBinding`. Remove the `new` modifiers.

## 4. Simplify `CronValidator`

Replace the 107-line hand-rolled parser with a shallow check: trim; accept the known macros
(`@hourly @daily @weekly @monthly @yearly @annually @midnight`); otherwise require exactly
5 whitespace-separated fields of characters `[0-9*,/-A-Za-z]`. Kubernetes is the authority on
full cron semantics — we only catch obvious typos at Build time. Keep the ITP201 code and
message. No new dependencies.

## 5. Add `[ConstantExpected]`

On the string parameters of `TopicRef<T>.Define`, `BindingRef.Define` (and any other
`Define`-style factories). Names must be compile-time constants per the design. Verify the
attribute produces CA warnings on non-constant arguments in a test snippet, then remove the
snippet (or keep as a commented example in the test file).

## 6. Rename: Workload → Component (align with the Backstage/Intropy runtime model)

"Component" is the canonical domain term (a deployable code container or job, part of a
System); "workload" is its Kubernetes runtime shape, not its identity. Rename the entity,
keep the attribute:

- `WorkloadModel` → `ComponentModel`; `SystemTopology.Workloads` → `Components`
  (still declaration order).
- `WorkloadType` enum **stays as-is** — "which k8s workload this component renders as"
  (Deployment/CronJob) is the correct use of the word.
- `TopicResource`/`BindingResource`/`ServiceResource`, `ComponentKind`, `ComponentBuilder`
  (or its staged successors): already correctly named, unchanged.
- Rule names/messages mentioning "workload" (e.g. `DuplicateWorkloadNameRule`,
  `ServiceWorkloadNameCollisionRule`) → rename to component terms; keep their ITP codes.
- Writing rule, apply in all XML docs and the README: bare "component" = the Intropy/Backstage
  entity; the Dapr resource is always fully qualified as "Dapr component".

Do this rename in its own commit, before or after the staged-builder work — not interleaved.
The snapshot test from acceptance criterion 4 must be updated in the same commit (property
name changes are expected there; values must not change).

## 7. Acceptance criteria

1. Solution builds with zero warnings; all tests green (`dotnet test`).
2. The following do **not compile** (verify manually, document in README):
   `AddLoader(...).Publishes(...)`, `AddAssembler(...).TriggeredBy(Trigger.Schedule(...))`,
   `AddExtractor(...).TriggeredBy(topicRef)`, a second `.TriggeredBy(...)` in one chain.
3. Rule count reduced; no rule inspects raw trigger-call lists anymore.
4. `SystemTopology` output for the README example is byte-identical before/after the refactor
   (materializer untouched) — add a serialization snapshot test first if none exists.
5. README example updated to the staged API; net library line count is lower than before.
