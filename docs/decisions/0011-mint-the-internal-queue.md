# 0011: The topology mints the transactional integration's internal queue

> Status: implemented.

**Background.** A transactional integration runs as two pipelines joined by a queue: its
receive pipeline reads the `From` connectors and publishes to an internal topic; its send
pipeline subscribes to that topic and writes the `To` connectors. That hop needs a Dapr
pubsub component at runtime, but the topology model recorded nothing about it — the
component builder's own documentation stated that a transactional integration "publishes
no topics."

The gap surfaced operationally. With nothing minted by the topology, the pubsub name came
from the component scaffold's `Constants.cs` (`PubSubName = "pubsub"`) and the Dapr
component YAML was expected in the component's `local/dapr-components/`. New scaffolds
have no such folder, so the sidecar loaded no pubsub under that name and the runner's
first publish failed with `pubsub pubsub is not found`. When a local file did exist, a
worse failure waited: the scaffold default `"pubsub"` is also the conventional name for
inter-component topic pubsubs, and the Aspire overlay replaces local resources with
generated ones of the same `metadata.name`. The generated topic pubsub is scoped to its
modeled publishers and subscribers — never the transactional integration — so the
integration's own hop silently became invisible to it.

**Decision.** The materializer mints an `InternalQueue` (`internal-<component>` pubsub,
`hop` topic) on every transactional integration's `ComponentModel`. Nothing declares it;
everything derives it from the model.

This narrows the earlier stance that the hop "never appears in the topology model." That
stance conflated two things that 0010 already separates. The hop is not an *edge*: no
other component may publish to or subscribe to it, so it never enters
`SystemTopology.Topics` and the DSL grows no builder surface for it. But its names are a
*minted fact* under 0010's own test — produced by the topology and consumed by backends
(local generation, Aspire, and the future deployment backend, which will need a broker
component for the hop in-cluster). Deriving the same names independently in each backend
would create exactly the multi-copy drift 0010 exists to prevent.

Under that test:

- **The DSL stays silent.** There is no `WithQueue`. A builder method would offer one
  degree of freedom — the name — and the name is a contract with nobody; offering it only
  reintroduces the scaffold-default collision.
- **The model carries the fact once.** `ComponentModel.InternalQueue` is minted at
  materialization and serialized, so `graph` output shows the hop and every consumer
  reads the same names.
- **Backends materialize, never invent.** Local generation emits the hop's pubsub
  component (Redis, scoped to exactly the owning component) and carries the names in the
  component's `.intropy.json`. The framework runner defaults
  `TransactionalIntegrationOptions.DaprPubSubName`/`DaprTopicName` from that config; the
  scaffolded `Constants.cs` value becomes a fallback rather than a second source of truth.

**Consequences.** A transactional integration runs with no `local/dapr-components/`
folder. The pubsub name can no longer collide with inter-component topic pubsubs, because
`internal-<component>` is minted from a DNS-1123 component name in a namespace the
generator controls. If a real choice about the hop ever appears (durability, a second
queue), the minted default becomes what a builder method overrides — the model shape
already admits it.
