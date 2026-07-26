# 0006: Contract-backed dev mocks via Microcks

> Status: implemented.

**Goal:** close the two dev-experience gaps ADR 0005 left open — HTTP-transport connectors pointed
at a placeholder URL that fails on first invocation, and no way to debug one component without
running every sibling — by serving contract-backed mocks locally.

## The design

[Microcks](https://microcks.io) serves REST mocks generated from OpenAPI contracts. The pattern —
contracts drive the simulation, Dapr edges point at it, internal siblings are simulated so a
developer runs only the component under debug — is the one described in the CNCF post
[Cloud-native app local development made easy with Microcks and Dapr](https://www.cncf.io/blog/2025/06/25/cloud-native-app-local-development-made-easy-with-microcks-and-dapr/)
(June 2025), adapted to the discover → generate → translate pipeline.

The dev seam stays **tool-neutral**; Microcks is an Aspire-side implementation detail, the same way
`MockExternalSystem` doesn't say "Docker". Two new declarations on `DevBuilder`:

- **`MockHttpExternalSystem(externalSystem, artifactPath, serviceName, serviceVersion)`** — dev
  generation points the system's `bindings.http` connector metadata `url` at the mock. Precedence
  in binding generation: explicit `ConnectorMetadata` (verbatim) > HTTP API mock > container-mock
  placeholder > empty. Non-HTTP transports ignore it (SFTP/blob keep their containers).
- **`MockApi(apiName, artifactPath, serviceName, serviceVersion)`** — dev generation emits a Dapr
  `HTTPEndpoint` resource named after the provider component's app-id with `baseUrl` = the mock URL.
  Dapr service invocation resolves the name to the endpoint, so consumers reach the mock unchanged.
  The Aspire backend then **does not start the provider** (or its sidecar). A provider is skipped
  whole, so every API it provides must be mocked; partial mocking and mocks naming unknown APIs make
  `Apply` throw with a listing (generation itself stays never-fail and just ignores unknowns).

The Aspire backend adds **one Microcks instance** (resource name `microcks`, official
`Microcks.Aspire` package, `microcks/microcks-uber` image) iff any contract-backed mock is declared,
imports every declared artifact into it, and gates components and sidecars on it like the other
backends (Redis, mock containers).

## Decisions

- **The URL convention is the one Generation-side compromise.** Generation stays dependency-free but
  emits URLs following the Microcks REST-mock shape, `http://localhost:{port}/rest/{service}/{version}`.
  The shape and the fixed host port (**8585**, Redis-6379 precedent — generate-before-run needs a
  known port) live in exactly one place: `DevHttpMock` in `DevProfile.cs`. Any non-Microcks fulfiller
  of this seam must serve mocks at those URLs.
- **One instance for all mocks.** Microcks namespaces services by `serviceName:version`; one
  container mirrors the single-`redis` infra pattern.
- **Official `Microcks.Aspire`, pinned 0.1.0.** It owns artifact upload lifecycle, health gating,
  and path resolution (relative artifact paths resolve against the AppHost directory). The
  dependency is confined to `Intropy.Topology.Aspire` and swappable behind `Apply` without touching
  the dev-seam model. The surface used is tiny: `AddMicrocks`, `WithMainArtifacts`, endpoint APIs.
- **`serviceName`/`serviceVersion` are declared, not derived.** They must match the contract's
  identity (`info.title`/`info.version` for OpenAPI) because the mock URL embeds them; deriving them
  would mean parsing specs in the dependency-free Generation package.

## Explicitly out of scope

- **Topic/pub-sub mocking.** Dev pub/sub is Redis (ADR 0005); Microcks' async minion speaks
  Kafka/MQTT/WebSocket/AMQP, not Redis. Revisit if the dev broker becomes pluggable.

  > **Amended 2026-07-25:** the dev broker is now RabbitMQ (ADR 0005, amended) — an AMQP broker
  > Microcks' async minion can speak. Topic mocking stays out of scope, but the protocol barrier
  > noted above no longer applies.

  > **Amended again 2026-07-25 (later):** the dev broker is back to Redis, on host port 6380
  > (ADR 0005, amended again). The original protocol barrier applies once more: Microcks' async
  > minion does not speak Redis, so topic mocking stays out of scope on protocol grounds too.
- **Contract-testing provided APIs** (`MicrocksClient.TestEndpointAsync`) — deferred until workers
  implement real endpoints; today they are stubs.
- **SFTP/blob external systems** keep container mocks (`atmoz/sftp`, Azurite).
- **Configurable Microcks port** (Redis's 6379 isn't configurable either) and **per-run dev-profile
  variants** (e.g. toggling `MockApi` per debug session) — follow-ups.
