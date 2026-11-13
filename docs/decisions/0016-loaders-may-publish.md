# 0016 — Loaders may publish messages

## Status

Accepted (2026-11-13).

## Context

`LoaderBuilder` had no `Publishes` method: a loader could subscribe to a channel and write
through a port, but never publish. This was asserted as the kind's contract throughout the
docs ("loaders publish nothing") rather than argued on any safety, ordering, or cycle-related
ground. Re-checking the actual model confirmed there was never a technical reason for it:

- `Component.AddPublish`/`PublishCalls` live on the base `Component` class, not on
  `ExtractorComponent` specifically — `LoaderComponent` already inherited them for free.
- No validation rule (`Validation/Rules/*.cs`) forbids a loader from publishing, and none
  rejects a component that both publishes and subscribes the *same* message on the *same*
  channel — a self-loop. The engine already tolerates this: `TopologyMaterializer`'s
  publisher/subscriber accumulators are `SortedSet`s (no double-counting), and
  `StartupOrdering.SubscriberPrecedence` already excludes a self-loop from ordering by design
  (`StartupOrdering.cs`, tested by `SubscriberPrecedence_WithSelfLoop_ShouldYieldNoEdge`).
- The restriction fell out of the extractor/loader/transactional-integration taxonomy
  (pull-and-publish / subscribe-and-push / read-and-write) rather than an argued constraint.

Two concrete needs motivated this, one more common than the other:

- **Receipts**: a loader writes to an external system and gets back an id or confirmation the
  system assigned — a different message than the one it consumed, typically picked up by a
  different component than the one that published what the loader subscribed to. This is the
  shape expected to come up most often.
- **Self-trigger**: a loader that wants to run a full-resync pipeline through the same
  ack/retry-backed machinery as every other message, by publishing a command to itself and
  also handling it on the channel it already subscribes to — rather than invoking that work
  in-process, out of band from Dapr's delivery and retry semantics.

## Decision

`LoaderBuilder` gets a `Publishes(MessageRef)` method, mirroring `ExtractorBuilder.Publishes`/
`GatewayBuilder.Publishes` exactly: a loader may publish zero or more distinct messages, the
same message may not be declared twice, and several published messages may share a channel.
Nothing else changes — `Subscribes`'s "one channel" rule is untouched, and bulk delivery
(`InBatches`) still applies to the whole subscription, not per message.

The receipt pattern — the common case, a different message for a different component:

```csharp
builder.AddLoader("order-loader")
    .Subscribes(Messages.Orders)
    .To(Ports.OrderLoaderDestination)
    .Publishes(Messages.OrderReceipt);
```

The self-trigger pattern — the same message, published and also handled on the loader's own
channel:

```csharp
builder.AddLoader("order-loader")
    .Subscribes(Messages.Orders, configure: sub => sub.AlsoHandles(Messages.ResyncRequested))
    .Publishes(Messages.ResyncRequested)
    .To(Ports.OrderLoaderDestination);
```

Both are the same `Publishes` call; nothing in the model distinguishes them.

No changes were needed in `TopologyMaterializer`, `Intropy.Topology.Generation`, or
`Intropy.Topology.Aspire` — all three already fold a component's `PublishCalls` generically
regardless of kind, so a loader's published messages flow into `ComponentModel.Publishes`, the
pubsub component's `scopes:`, and the JSON graph contract exactly like an extractor's already
do.

## Consequences

- Every doc asserting "loaders publish nothing" (`getting-started.md`, `components.md`,
  `model.md`, `system-model.md`, `declaration-language.md`, `README.md`) now describes
  publishing as legal and documents both the receipt and self-trigger patterns as worked
  examples.
- A loader that both publishes and subscribes the same message on one channel is a supported,
  documented shape, not an incidental one the engine happens not to reject.
- This decision does **not** touch a separate, harder question considered and rejected during
  the same work: letting a loader subscribe to more than one independent *channel*. A live
  test against this project's actual Redis pubsub backend showed two declarative Subscription
  resources on one topic (one bulk-enabled, one not) deliver incorrectly — confirmed
  reproducible, not theoretical. That idea stays out of scope; a loader still subscribes to
  exactly one channel, now with publishing allowed on it.
