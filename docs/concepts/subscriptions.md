# Subscriptions

A subscription is a component's subscription to one channel: the messages it handles from that
channel, and what happens to the channel's other messages.

```csharp
builder.AddLoader("fulfillment")
    .Subscribes(sub => sub
        .Handles(Messages.OrderPlaced)
        .Handles(Messages.OrderCancelled, when: "event.data.reason == 'customer-request'")
        .IgnoreOthers())
    .To(Ports.Wms);
```

- **Handles** — a message the component processes. The channel is the messages' own: they must all
  travel on the same pubsub and topic (a channel may carry several messages, each with its own
  contract). A message's name is its CloudEvent type, so the subscription can route on it.
- **Handles(message, when:)** — a content filter: a Dapr CEL expression over the event that the
  message's events must also match, such as `event.data.reason == 'customer-request'`. The
  framework publishes payloads as camelCase JSON objects, so properties read `event.data.orderId`.
  The message's events the filter leaves out are handled like the channel's other messages
  (below). A filter is a string the sidecar evaluates; the topology does not check it.
- **Others** — by default, a message on the channel that the subscription does not handle is left
  for redelivery, so the broker dead-letters it: a message nobody expected is visible and can be
  replayed. `IgnoreOthers()` acknowledges and drops them instead, for a channel that carries
  messages meant for other components.
- **InBatches(maxMessages, maxWait)** — the sidecar delivers the subscription's messages in batches
  (Dapr bulk subscribe). It is declared on the subscription because batching configures how the
  sidecar delivers that subscription's messages.
- `Subscribes(message)` is shorthand for a subscription handling that one message.

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

| Severity | Rule |
|----------|------|
| Error | A subscription's messages must all travel on one channel. |
| Error | A subscription must handle at least one message. |
| Error | A component must not handle the same message more than once. |
| Error | A loader subscribes to exactly one channel. |

## Related

- [Messages](messages.md)
- [Validation](validation.md)
- [ADR 0013](../decisions/0013-subscriptions-render-as-dapr-subscriptions.md)
