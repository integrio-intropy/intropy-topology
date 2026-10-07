# Subscriptions

A subscription is a component's subscription to one channel: the message `Subscribes` requires,
the channel's other messages `AlsoHandles` adds, and what happens to the channel's other messages.

```csharp
builder.AddLoader("fulfillment")
    .Subscribes(Messages.OrderPlaced, configure: sub => sub
        .AlsoHandles(Messages.OrderCancelled, when: "event.data.reason == 'customer-request'")
        .IgnoreOthers())
    .To(Ports.Wms);
```

- **Subscribes(message, when?)** — the message the component processes, and the subscription with
  it. The channel is the message's own: every handled message must travel on the same pubsub and
  topic (a channel may carry several messages, each with its own contract). A message's name is
  its CloudEvent type, so the subscription can route on it.
- **AlsoHandles(message, when?)** — a message the component additionally processes, through the
  `configure` parameter. All handled messages must travel on the subscription's channel.
- **AlsoHandles(message, when:)** — a content filter: a Dapr CEL expression over the event that the
  message's events must also match, such as `event.data.reason == 'customer-request'`. The
  framework publishes payloads as camelCase JSON objects, so properties read `event.data.orderId`.
  The message's events the filter leaves out are handled like the channel's other messages
  (below). A filter is a string the sidecar evaluates; the topology does not check it. Both the
  required message and an added one can carry a filter.
- **Others** — by default, a message on the channel that the subscription does not handle is left
  for redelivery, so the broker dead-letters it: a message nobody expected is visible and can be
  replayed. `IgnoreOthers()` acknowledges and drops them instead, for a channel that carries
  messages meant for other components.
- **InBatches(maxMessages, maxWait)** — the sidecar delivers the subscription's messages in batches
  (Dapr bulk subscribe). It is declared on the subscription because batching configures how the
  sidecar delivers that subscription's messages.

Because `Subscribes` requires its message, an empty subscription cannot be declared.

Because each subscribing component gets its own consumer group, Dapr still delivers every message
on the channel to this component — a message another subscriber handles is not "taken" by them;
it dead-letters here too when not handled. `Build()` therefore warns when a subscription keeps the
dead-letter default while its channel carries messages it leaves unhandled, naming every one of
them: a loader handling one message on a channel carrying three would otherwise dead-letter the
other two with no signal in the declaration. Handle the warnings' messages with `AlsoHandles`, or
declare `IgnoreOthers()` as the explicit acknowledgment that unhandled messages are dropped. The
warning never fires for a channel whose messages the subscription handles completely, and a
content filter does not affect it — the rule works at message granularity. As with the one-sided
topic warnings, deployment validation may promote this warning to an error.

The topology says *which* messages a component handles; the component's code says *how* — one
pipeline per message, registered under the same message names.

## What it renders

Each subscribing component gets one declarative Dapr `Subscription`, named
`<component>-subscription` and scoped to the component:

```yaml
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: "fulfillment-subscription"
spec:
  pubsubname: "pubsub"
  topic: "orders"
  routes:
    rules:
    - match: "event.type == 'fluxia.orders.order-placed'"
      path: "/fluxia.orders.order-placed"
    - match: "event.type == 'fluxia.orders.order-cancelled' && (event.data.reason == 'customer-request')"
      path: "/fluxia.orders.order-cancelled"
    default: "/unhandled"
scopes:
- "fulfillment"
```

The default route is always rendered: without one, the sidecar acknowledges and discards an
unmatched message silently. With it, the component sees the message and applies its choice —
dead-letter (leave it for redelivery) or ignore (acknowledge it). The component treats a message
delivered on the default route as unhandled whatever its type, so a content filter holds even
though a pipeline handles the message's type.

The names come from one place, `SubscriptionRouting` (`ResourceNameFor`, `PathFor`, `MatchFor`,
`UnhandledPath`), so every backend renders the same resource. A transactional integration's
internal hop carries one kind of message; its `Subscription` has the default route only.

The component's runtime config (`<component>.intropy.json`) carries the same facts:
`Subscribes[].Messages`, `Subscribes[].Conditions` and `Subscribes[].Unhandled`.

## Rules

The invariants local to one subscription throw where they are declared, as `InvalidOperationException`:

| Thrown at | Invariant |
|-----------|-----------|
| `Subscribes` | A loader declares its subscription once; the handled message's channel is the subscription's channel |
| `AlsoHandles` | A message name is handled once per subscription |
| `AlsoHandles` | Every handled message travels on the subscription's first message's channel |

What remains for `Build()` is the loader that never subscribes at all — a cross-declaration completeness rule:

| Severity | Rule |
|----------|------|
| Error | A loader subscribes to a channel. |

## Related

- [Messages](messages.md)
- [Validation](validation.md)
- [ADR 0013](../decisions/0013-subscriptions-render-as-dapr-subscriptions.md)
