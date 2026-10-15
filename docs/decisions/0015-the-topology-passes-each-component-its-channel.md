# 0015 — The topology passes each component its channel

## Status

Accepted (2026-10-15). Supersedes the "framework derives the same names" bullet of
[0014](0014-one-pubsub-per-system.md); the names 0014 mints stand.

## Context

0014 had the framework derive a component's pubsub, `<system>-pubsub`, from
`INTROPY_SYSTEM_NAME`, and a transactional integration's topic from its component name. The
framework (2.0.0-beta.1) dropped that derivation: it reads the pubsub from `INTROPY_PUBSUB_NAME`
and the topic from `INTROPY_TOPIC_NAME` and derives nothing. The host kept setting only
`INTROPY_SYSTEM_NAME`, so every component failed at startup with its pubsub unset.

A derivation in the framework also duplicated the topology's: a message naming another pubsub
with `pubSub:` resolved differently in the two.

## Decision

- **`ComponentChannel.For` is the single derivation site.** A transactional integration runs on
  its internal queue. Any other component runs on the pubsub all its channels share, and on the
  topic of its only channel; a name with no single answer is left unset.
- **The Aspire host sets `INTROPY_PUBSUB_NAME` and `INTROPY_TOPIC_NAME`** from it, and no longer
  sets `INTROPY_SYSTEM_NAME`.
- **The graph record carries it** as each component's `channel` (`pubsub`, `topic`), so deployment
  tooling sets the same variables without re-deriving them.

## Consequences

- Code that read `INTROPY_SYSTEM_NAME` under the local host no longer gets it.
- Deployment templates must set both variables from the record's `channel`.
