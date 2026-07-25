# 0008: Topic completeness is validated; a loader's destination may stay private

> Status: implemented.

**Background.** The system tutorial (the devex design instrument for this library) declares its
minimal system as an extractor and a loader sharing one `TopicRef` — and the loader has no `To`
call, because its destination is a folder binding in the integration's own
`local/dapr-components/`, a concern the system never needs to know. Under the rules as they
stood, that system failed ITP206 (`Loader components must write to an external system`) — while a
subscription to a topic nobody publishes, a contract that can never be honoured, passed silently.
The rules enforced the wrong invariant on both sides.

**Decision (two halves, one principle).** The topology validates the edges *between* components;
it does not demand edges *out* of them.

1. **ITP206 no longer applies to loaders.** External edges enter the topology when the *system*
   needs to know them (a shared connector, a resolvable transport, a generated binding) — the same
   progressive-resolution stance as ADR 0007. A loader whose destination stays a private local
   component is a legal, complete participant. Extractors and aggregators still must publish:
   a block whose entire purpose is feeding the system's topics is incomplete without one.

2. **New rules ITP208/ITP209 enforce topic completeness, both as errors.**
   - **ITP208**: a topic is published but no component subscribes to it.
   - **ITP209**: a topic is subscribed to but no component publishes it.

   Within one system's closed world, both halves of an orphaned topic are defects, not fan-out
   reserves: the model has no "external consumer" concept, so an unconsumed publish is a message
   into the void and an unproduced subscription never fires. A mistyped topic ref surfaces as the
   pair — the real topic orphaned (ITP208) and the misspelled one unproduced (ITP209) — which is
   exactly the drift the typed-ref design exists to catch at `Build()` instead of at runtime.

**Why errors, not warnings.** ITP209 is unconditionally broken. ITP208 could in principle be a
deliberate seam for a future consumer — but the declaration is cheap to change in the same file
where the consumer will be added, and a silent void-publish is the tutorial's canonical
"used to live until runtime" bug. If cross-system topics ever become a modeled concept, ITP208 is
where the exemption would land.

**When this would change.** If connectors become required for prod generation completeness
(a loader with no `To` and no local component has nowhere to write), that is a *generation-profile*
concern — a future Prod-profile diagnostic — not a topology-completeness rule.

## Mechanics

- `MissingRequiredOutputRule` (ITP206) drops its loader arm; extractor/aggregator arms unchanged.
- `UnconsumedTopicRule` (ITP208) and `UnproducedTopicRule` (ITP209) read the materialized
  `TopicResource.Subscribers`/`Publishers` (already precomputed on both sides).
- The living-spec sample (`EndToEndTests`) gained `price-extractor` publishing `price-raw` —
  previously subscribed with no publisher, a latent ITP209 the old rules never saw — and the
  byte-exact snapshot was regenerated.
