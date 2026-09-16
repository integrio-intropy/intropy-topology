# Topology Declaration Language

The topology language describes what belongs to an integration system and how its parts connect. A declaration describes structure, not a sequence of instructions.

This page defines the current concepts and rules without choosing a programming language, file format, or runtime. The [System Model](system-model.md) introduces the entities. The [implementation reference](model.md) describes how they are expressed in C#.

## What can be declared

| Declaration | Information it carries |
|-------------|------------------------|
| System | A name and a collection of components. |
| Component | A name, a kind, and its relationships. |
| Message | A name, a payload contract, and a channel. |
| Port | A name identifying an external connection point. |
| Service | An identity for an external provider that components invoke. |

A payload contract describes the data carried by a message. A channel identifies a messaging resource and a topic within it. The logical message name and topic name may differ.

Components belong to one system. Ports are system-owned. Used messages are grouped under the system. Services are dependencies, not components owned or deployed by the declaration.

Naming a message, port, or service makes it available for use. It becomes part of the resulting topology only when a component refers to it. Unused definitions do not create connections or resources.

## What can be stated

The language has five relationship statements:

| Statement | Meaning |
|-----------|---------|
| A component publishes a message | The component produces data under that message's contract. |
| A component subscribes to a message | The component consumes data from that message's channel. |
| A component reads from a port | The port is an input connection for the component. |
| A component writes to a port | The port is an output connection for the component. |
| A component uses a service | The component invokes the external provider. |

A port's direction follows from these statements; it is not fixed by the port's identity. The same port can be used by several components and in both directions.

A relationship does not specify when work starts, how data is transformed, or how failures are retried. It also does not prescribe execution order or delivery guarantees.

## Which relationships are allowed

Component kind determines the allowed relationships and their counts:

| Component kind | Reads from ports | Writes to ports | Publishes messages | Subscribes to messages | Uses services |
|----------------|------------------|-----------------|--------------------|-----------------------|---------------|
| Extractor | Zero or more | None | One or more | None | Zero or more |
| Loader | None | Zero or more | None | Exactly one | Zero or more |
| Transactional integration | One or more | One or more | None | None | Zero or more |

An extractor without a declared input port keeps its source inside the component. A loader without a declared output port keeps its destination inside the component. Absence of a port means no shared connection is declared, not that the component has no source or destination.

A transactional integration has an internal hand-off between receiving and sending. It is not a public message relationship and cannot be connected to other components through this language. Its name does not, by itself, promise an atomic transaction across external systems.

## Consistency and completeness

A declaration must satisfy these rules:

- A system contains at least one component, and component names are unique within it.
- Each component satisfies the relationship counts for its kind.
- One message name identifies one payload contract and one channel within the system.
- Messages sharing a channel use the same payload contract.
- A component does not declare publication or subscription to the same channel twice.
- A component does not declare use of the same service twice.
- Repeating a read or write relationship to the same port adds no new relationship. Reading and writing remain distinct.
- An external service identity does not identify a component in the same system.

A channel should have at least one publisher and one subscriber. An unfinished system may contain only one side. This is reported as an incomplete connection rather than a contradiction; it must be distinguished from a deployment-ready system.

An implementation must distinguish errors from warnings and report the affected declarations. Whether a rule is enforced during editing, compilation, or validation is an implementation decision.

## Settings outside the relationship model

Two supporting declarations accompany the topology:

- **Telemetry export:** a system may select one export destination, with a protocol and request headers. With no selection, runtime defaults remain in effect. The declaration does not resolve secret values.
- **Local development:** used ports may be mapped to local folders, and used services may be replaced by contract-backed mocks. A local development declaration resolves every used port. It cannot introduce topology connections or change component kinds.

Schedules, connection credentials, deployment addresses, replicas, and resource limits are not relationship declarations. They belong to workload or environment configuration.

## Result of a declaration

The result describes:

- the system's components and their relationships;
- used messages, their contracts and channels, and their publishers and subscribers;
- used ports, their directions, and the components using them;
- used services and their consumers;
- optional system-wide telemetry settings.

These are views of the same declaration, not separate facts to maintain. For example, a message's list of subscribers is derived from subscription statements.

## Example in words

The order-flow system contains an order extractor and an order loader.

The extractor reads from the order source port and publishes order messages. The loader subscribes to those messages and writes to the order destination port. Both components use an idempotency service.

This declaration says nothing about the source's address, the extraction schedule, or the destination's credentials. Those choices do not change the relationships.

## Evolving the language

A conceptual change should state which declarations or relationships it adds or changes, what they mean, which combinations are valid, and how existing declarations are affected.

Those decisions can be reviewed here before changing an implementation. Method names, type layouts, name-format restrictions, serialization, and runtime resource mappings belong in the implementation reference. A change to the concept is not a claim that an implementation already supports it.
