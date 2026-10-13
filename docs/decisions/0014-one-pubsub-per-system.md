# 0014 — One pubsub per system; internal queues get their own topics

## Status

Accepted (2026-10-13). Supersedes the names minted in
[0011](0011-mint-the-internal-queue.md); minting the internal queue in the model stands.

## Context

Two assumptions behind the minted names did not hold for deployment.

**A Dapr component does not separate traffic on the broker.** 0011 minted each transactional
integration its own pubsub component, `internal-<component>`, all with the topic `hop`.
Brokers name their resources after the topic, not the component: Dapr's RabbitMQ component
declares an exchange per topic and a queue `<app-id>-<topic>` per subscriber, and Service Bus
has one topic entity per topic. Every integration publishing to `hop` on a shared broker
therefore published into one fanout exchange, and each integration received the others' files.
Local runs never showed it: Redis streams separate consumers by group, and each run starts
clean.

**Several systems share a namespace and a broker.** An environment is one Kubernetes
namespace, with one RabbitMQ vhost or Service Bus namespace, and every system in that
environment deploys into it. Dapr components and app IDs are namespace-scoped, so a component
name every system renders — the `MessageRef` default `pubsub` — had several owners, and only
one system's apps in its scopes. A component per environment instead would leave no owner
able to keep its scopes current, and every system would share one broker credential.

## Decision

- **Each system owns one pubsub component, `<system>-pubsub`.** A message that names no
  pubsub resolves to it when the topology is built (`MessageRef.PubSubName` is null until
  then). It is scoped to the system's components and carries the system's broker credentials;
  every system's component points at the environment's shared broker.
- **The internal queue rides the system's pubsub on its own topic, `internal-<component>`.**
  The topic is what keeps queues apart on the broker. App IDs are unique in a namespace, so the
  topic is unique across every system in an environment too.
- **Backends include the integration in the scopes of the system's pubsub.** A system of only
  transactional integrations has no topics, yet still gets its pubsub.
- **The framework derives the same names**, from `INTROPY_SYSTEM_NAME` and the component name.
  Local runs set the variable; deployment sets it from the topology.

Naming another pubsub with `pubSub:` stays possible, for a component the system does not own.

## Consequences

- Message topics are shared across an environment: two systems that publish a message named
  `orders` publish into one exchange. Message names must be unique per environment, which is
  also what lets one system consume another's released messages.
- Changing names gives existing deployments new queues; drain the old `hop` queues before
  upgrading.
- A port's Dapr binding component is also namespace-scoped and named after the port, so port
  names share the same environment-wide namespace. This record does not change that.
