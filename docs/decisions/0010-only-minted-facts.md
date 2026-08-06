# 0010: The topology carries only minted facts

> Status: implemented.

**Background.** The topology exists so that one declaration can be trusted: a developer reads
`graph` output or the SystemHost and believes what it says about the system. That trust has a
structural enemy — any fact the topology *repeats* from configuration that deployment owns. Two
copies of one fact drift, and the first discovered lie poisons everything else the model says.

The cron schedule was the case that surfaced it. `WithSchedule` declared the deployed CronJob
cadence, but that cadence lives in the GitOps repo, where developers will edit it over time without
touching the SystemHost. The system keeps working; the topology silently starts to lie. The same
shape appeared in the connector `Transport`: the declared binding type is deployment-owned, and the
local generator never even reads it — `Generation.cs` carried a comment explaining that the
declared transport is "deliberately not consulted." A model property the generator must be careful
not to read is not a model property; it is a liability wearing a property's clothes.

ADR 0004 had already decided this once: the trigger concept was deleted because "the cron
expression and the resulting CronJob vs. Deployment choice are purely *inside* a component... Only
edges belong in a topology," with cron moving to the component's own scaffold. `WithSchedule`
crept back as an attribute and reintroduced exactly the problem 0004 removed. This decision
completes 0004 rather than reversing it.

**Decision.** The topology carries only facts it *mints*; it never repeats facts deployment owns.

The test is ownership, not observability. "Can someone change this in GitOps and have the system
still work?" is the wrong question — it is time-dependent, and the dangerous facts pass it
silently for days or forever. The right question: **is this fact produced by the topology and
consumed by deployment, or owned by deployment and merely repeated here?** Only the first kind
belongs in the model.

Under that test:

- **Minted identities stay.** Component names, service app-ids, pubsub names, and the derived
  binding name (`binding.<connector>`) are created by the topology: the Aspire backend
  materializes them locally, generated artifacts carry them, and deployment honors them. Breaking
  one is loud — a renamed app-id fails service invocation at runtime.
- **Edges stay.** `Subscribes`/`Publishes`/`From`/`To`/`Uses` are compile-enforced claims about
  the code itself. A deployment that contradicts them is a broken deployment, loudly.
- **The connector transport goes.** The Dapr binding's `spec.type` is deployment-owned
  configuration, exactly like the credentials the model already excludes. Locally it was dead
  weight; deployed it was a second copy waiting to drift. Its removal also collapses connector
  identity to the name alone, making divergent same-name declarations structurally impossible.
- **The schedule goes.** Cron is deployment policy — per-environment by nature — and 0004's
  destination was the component scaffold. It returns there and nowhere else: the model does not
  re-home it into the development manifest, because that would split the fact (one cron for F5,
  another in GitOps) instead of giving it one home. Local runs still start every component once;
  recurring activation, local or deployed, is a scaffold concern.

The flow contract that keeps the remaining facts honest: **topology → generated artifacts →
GitOps.** Deployment consumes the names the topology mints; it never maintains its own copy.

**Known limitation.** `ServiceRef` app-ids are unqualified, while deployed Dapr resolution is
namespace-scoped. Within the system's own namespace the minted identity holds; cross-namespace or
cross-system invocation would need qualified app-ids and is out of scope until a concrete need
appears.

**Consequences.** The model shrinks to components, edges, and minted identities — exactly what it
can guarantee. Anything deployment-owned (binding types, schedules, credentials, endpoints) has
one home in the deployment configuration. If verification of declared intent against live cluster
state is ever wanted, it belongs in deployment tooling comparing expectations to reality — not as
repeated facts in this model.
