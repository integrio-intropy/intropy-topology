# Per-block components and framework-style layout

> Status: implemented

**Goal:** make the library graspable by mirroring the conceptual pattern of
`intropy-framework`, which the team already knows: there, `Pipeline` is the shared
mechanism and each block is a folder holding an immutable artifact (`Extractor`) plus its
builder (`ExtractorBuilder`). Here the same roles existed but were hidden — the shared
concept was an invisible `internal ComponentCore`, no per-block artifact existed at all,
and Assembler/Reconciler shared one `Aggregator*` builder pair distinguished only by a
kind enum.

**Unchanged:** the serialized model (`ComponentModel`, `ComponentKind`, `SystemTopology`
JSON — the `EndToEndTests` snapshot is byte-identical), staged-builder compile-time
legality (trigger stages still expose *only* the legal `TriggeredBy` overloads; ITP203–205
stay retired), `TopologyMaterializer` semantics, and all ITP codes except one guard noted
below.

## Decisions

### `Component` is both the shared state and the public base

`ComponentCore` became `public abstract Component` in the root namespace. It is
simultaneously the recording state holder and the base of the per-block components
(`ExtractorComponent`, `AssemblerComponent`, `ReconcilerComponent`, `LoaderComponent`,
`TransactionalIntegrationComponent`, `ContainerServiceComponent`). The framework composes
(`Extractor` *wraps* a `Pipeline` chain); here inheritance is the idiomatic C# shape
because the wrapped state is uniform across blocks — a separate wrapper would hold nothing
but a reference. Public surface in v1 is `Name` + `Kind` (+ `Image` on container
services); the recorded calls and mutators are `internal`, the constructor is
`private protected`, so the hierarchy is closed and the staged builders are the only
writers. Richer public views (typed trigger/edge lists) are additive later.

### The handle surfaces as a `Component` property on the block builder

```csharp
ExtractorComponent orders = s.AddExtractor("order-extractor")
    .TriggeredBy(...).From(...).Publishes(...).Component;
```

The system-level terminal stays `SystemBuilder.Build()`; the per-block artifact is
registered at `Add*` time, so the builder *hands you* the artifact rather than producing
it with a terminal call. Rejected alternatives: implicit conversion (invisible, breaks
`var`), builder-is-component (collapses the builder/artifact split being mirrored), and a
mandatory terminal call (ceremony that changes nothing when forgotten). Trigger stages do
**not** expose the property — "the stage's only members are the legal `TriggeredBy`
overloads" stays literally true.

### Distinct Assembler and Reconciler builders, no shared base

The `Aggregator*` pair is gone. The two builders are near-clones (~10 lines each); a
shared generic base would need an abstract builder factory — more machinery than it
removes. Their grammars can now diverge without affecting each other.

### Duplication over hierarchy: no `PublishingComponentBuilder`

The middle CRTP base whose entire content was two `Publishes` overloads shared by three
builders was deleted; each publishing builder (Extractor, Assembler, Reconciler) carries
the overloads directly. Same reasoning as above — a capability-classifying base is more
machinery than ~15 duplicated lines — and it matches the framework, whose builders have
no base classes at all. `ComponentBuilder<TSelf, TComponent>` stays because its members
(`Component`, `Uses`) are universal to every builder, not a classification.

### One folder per block under `Building/`, components in the root

> **Amended 2026-07-19:** the per-block folder/sub-namespace layout was reverted the same
> day in favor of the grouped-file convention the repo used before the framework-convention
> alignment: all builders and trigger stages live in `Building/ComponentBuilders.cs` under
> the single `Intropy.Topology.Building` namespace, the component hierarchy in
> `Components.cs`, refs in `Refs.cs`, and the model regrouped into `Edges.cs` /
> `Resources.cs` / `Enums.cs`. The *type design* of this ADR (Component hierarchy, typed
> handles, distinct Assembler/Reconciler builders) is unchanged; only the file/namespace
> layout below is superseded. Rationale: the one-type-per-file split roughly doubled the
> file count for ~30% more code, and the sprawl cost more readability than the framework
> mirroring bought.

`Intropy.Topology.Building.Extractor.{ExtractorTriggerStage, ExtractorBuilder}` parallels
the framework's `Blocks/Extractor/{Extractor, ExtractorBuilder}`. Namespace == folder
holds; fluent call sites never name these namespaces (only `SystemBuilder.cs` imports
them). The per-block *components* deliberately live in the root namespace instead of the
block folders — they are the one per-block type users do name, and `Building` remains
machinery users never name. This is the single deviation from perfect framework mirroring.

### Container services normalized onto the shared path

`SystemBuilder` holds one declaration-ordered `List<Component>`; the separate
`_containerServices` list is gone and the materializer dispatches on
`ContainerServiceComponent`. This also fixes an undocumented quirk: containers used to
materialize last regardless of declaration position; now declaration order is honored
for every component. ITP202 (missing trigger) gained a guard — container services
legally have no trigger.

## Breaking changes (pre-1.0)

- `ComponentBuilder<TSelf>` gained a `TComponent` type parameter;
  `PublishingComponentBuilder<TSelf>` was removed (its `Publishes` overloads live on the
  publishing builders directly).
- `AggregatorTriggerStage` / `AggregatorBuilder` removed (use the Assembler/Reconciler
  types).
- Stage/builder types moved into per-block sub-namespaces of `Intropy.Topology.Building`.
- `ContainerServiceBuilder.Name` / `.Image` moved to `builder.Component`.
- `SystemBuilder.ContainerServices` (internal) removed.

Pure fluent call sites compile unchanged.
