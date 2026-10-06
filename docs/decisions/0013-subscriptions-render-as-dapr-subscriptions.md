# 0013 — Subscriptions render as declarative Dapr Subscriptions

## Status

Accepted (2026-10-01).

## Context

The framework's subscribing blocks (loaders, transactional integrations) receive their messages
through the Dapr gRPC app callback only: the sidecar pushes, and the subscription is a declarative
resource the block does not announce. A channel may carry several messages, each with its own
contract, and a component handles a subset of them. Which messages a component takes from a
channel is a fact about how components connect, so it belongs in the topology; how each message is
processed stays in the component's code.

## Decision

- A subscription is declared as `Subscribes(message, configure: sub => …)`; the required message
  is a parameter of `Subscribes` itself and `configure` adds to it. The subscription's channel is
  the handled messages' own; they must share it.
- A message's name is its CloudEvent type. Routing rules match `event.type` against it.
- Every subscribing component renders one `Subscription`: a rule per handled message to
  `/<message>`, and always a default route `/unhandled`. Without a default route the sidecar
  silently acknowledges and discards unmatched messages (verified against daprd 1.18); with it,
  the component decides — leave for redelivery so the broker dead-letters it (the default), or
  acknowledge (`IgnoreOthers()`).
- No Dapr `deadLetterTopic`: dead-lettering stays with the broker's own queue, where
  `message-resender` replays it.
- A handled message may carry a content filter, `AlsoHandles(message, when: "<CEL>")` (or the
  `when:` parameter of `Subscribes`), rendered into
  its rule as `event.type == '<message>' && (<CEL>)`. Publishers send an object payload as a
  camelCase JSON object, so the rule reads `event.data.<property>` (verified against daprd 1.18).
  A filtered-out event takes the default route and is unhandled like any other: the framework
  treats a delivery on `/unhandled` as unrouted whatever its type, so the filter cannot be
  bypassed by the component's routing on type. The expression is opaque to the topology; the
  sidecar rejects an invalid one when it loads the `Subscription`.
- The rule "messages sharing a channel use the same payload contract" is dropped.

## Consequences

- The names are derived in one place (`SubscriptionRouting`) so local generation, Aspire,
  deployment tooling and the framework agree.
- Every message-receiving component gets a gRPC app channel locally, not only batching loaders.
- Deployment tooling renders the same resource from the graph's `subscribes[].messages`.
