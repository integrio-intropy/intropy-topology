# 0009: Reduce the library to the minimal model the system tutorial requires

> Status: implemented (2026-07-25).

**Background.** The library had grown a surface well ahead of what any consumer exercised. The
system tutorial (the devex design instrument for this library) and `intropy sys create` — the two
things that actually consume the DSL today — need exactly three block kinds, one topic per
publisher, and file connectors. Everything beyond that was speculative: designed carefully, tested,
and used by nobody. Unused surface is not free — every rule, transport, and builder member is
something the tutorial's reader can wander into, something the CLI templates must not accidentally
depend on, and something each subsequent change must keep consistent.

**Decision.** Cut the library to the minimal model, on the deterministic-over-complexity principle:
keep only what the tutorial and `intropy sys create` demonstrably require; surface the rest as an
explicit gap rather than carrying it speculatively.

What was cut:

- **Aggregator** — the block kind, its builder, and its component subclass.
- **APIs** — `ApiRef<T>`, `Provides`/`Consumes`, `ApiResource`, and ITP501.
- **Named publish ports** — `Publishes(port, topic)`; a component now publishes at most one topic,
  and ITP101 re-purposes from port collisions to "more than one `Publishes` call is an error".
- **Non-file transports** — `Transport.Http()`/`Sftp()`/`Blob()`/`Custom(...)` and the direction
  capability model behind them (ITP105/ITP106). The single remaining transport,
  `Transport.File(rootPath)` (`bindings.localstorage`), reads and writes, so capabilities check
  nothing.
- **Unresolved connectors and `ExternalSystem`** — `ConnectorRef.Define(name, transport)` is the
  only overload; ITP107 retires with the resolution lifecycle (ADR 0007's deferral mechanics go
  with it; its progressive-resolution stance survives as the file-transport default). A file
  connector's folder is its endpoint, so no external-system concept is needed.
- **Dev mocking** — the `IDevProfile` seam and the Microcks integration (ADR 0006's machinery);
  the Aspire backend runs only the dev broker (Redis), projects, and sidecars.
- **The Prod generation profile** — `TopologyGenerator.Generate(topology)` takes no profile; the
  verbs are `check | graph | generate [dir]` with no `--profile`.

Retired diagnostic codes — ITP105, ITP106, ITP107, ITP501 — join ITP201–205 in the never-reuse
list.

**Why now.** The tutorial is the forcing function: it must present a surface a reader can hold in
their head, and the CLI templates must compile against exactly what they generate. Trimming after
the fact is cheaper than explaining, testing, and template-proofing features nothing uses. This is
the same call as the CLI's local-first normalization: borrow what reliably works, cut the rest,
and record the gap.

**The full model is preserved, not deleted.** The pre-cut library lives on the **`full-topology`**
branch. When a cut feature is validated by a real need, re-adding it starts from a cherry-pick or
merge of that branch — not a rewrite. The branch carries the tested implementation, the validation
rules, and the snapshot expectations; reimplementing from memory would rediscover its bugs one at
a time.

## Mechanics

- `Components.cs`, `Building/ComponentBuilders.cs`, `Refs.cs`, and the model records dropped the
  cut members; `SystemTopology` is `{ SystemName, Components, Topics, Connectors }`.
- `ConnectorResource` is `{ Name, Transport (non-null), DaprComponentName (derived), Directions,
  UsedBy }`.
- The example became `examples/OrderFlow.SystemHost` (order-extractor + order-loader +
  `OrderFlow.Contracts`), mirroring `intropy sys create` codegen verbatim.
- The byte-exact JSON snapshot in `EndToEndTests` was regenerated for the reduced model.
