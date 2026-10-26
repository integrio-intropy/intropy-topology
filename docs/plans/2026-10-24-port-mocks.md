# Plan (sketch): mock external APIs behind ports with Microcks

## Goal

Let a port (`From`/`To`) whose deployed counterpart is an HTTP API resolve locally to a
Microcks mock, the way a `Calls` service already does. Today every port must resolve to a
folder (`development.Files(port).RootPath(...)`); `DevelopmentManifest.Build()` rejects any
port without one.

## Non-goals

- No change to the topology model or `SystemTopology` JSON. A port's deployed binding type is
  environment-owned; how it is substituted locally is development data, like service mocks
  and file roots ([0010](../decisions/0010-only-minted-facts.md)).
- No AsyncAPI / event mocking — REST over OpenAPI 3.0.x only, the same contract rules as
  service mocks.

## Proposed surface

```csharp
public sealed class OrderFlowDevelopment : IDevelopmentDefinition
{
    public void Define(DevelopmentBuilder development)
    {
        development.Mock(Ports.ErpOrdersSource).FromOpenApi("mocks/erp-orders.openapi.yaml");
        development.Files(Ports.OrderLoaderDestination).RootPath("./test/order-loader-destination");
    }
}
```

`Mock(PortRef)` is an overload beside `Mock(ServiceRef)` and reuses `MockBuilder`.

## Changes by layer

**Generation — `Development.cs`**

- `DevelopmentBuilder.Mock(PortRef)`: rejects ports the topology does not use, a second mock
  for the port, and a port that has both a mock and a file resolution (and `Files` rejects the
  reverse).
- `MockDeclaration` / `OpenApiMock` gain a subject discriminator (service app ID vs port
  name) instead of a bare `AppId`. The Microcks identity collision check (`Title`, `Version`)
  spans both kinds.
- `Build()`: a port is resolved when it has a file root *or* a mock. The "no local file
  resolution" diagnostic becomes "no local resolution".
- `DevelopmentManifest` either gains `PortMocks` or `Mocks` becomes a list over both subjects.
  Prefer one list: the Aspire importer and readiness gate already iterate it.

**Generation — `Generation.cs`**

- `BindingComponent` switches on the port's resolution: localstorage for a folder, an HTTP
  binding for a mock (see the open question), with `url` = `LocalMockEndpoints.BaseUri(mock)`
  and the same `port.DaprComponentName` and `UsedBy` scopes. The component's
  `.intropy.json` (`DaprComponent` per port) is unchanged, so component code keeps addressing
  the port by the same binding name.
- `graph --development` serializes port mocks next to file resolutions.

**Aspire — `Microcks.cs`, `IntropyAspire.cs`**

- `MockReadiness` keys gates by subject, not app ID. The startup wait (`IntropyAspire.cs`
  ~127) waits on `component.Uses` *plus* the component's mocked ports.
- `MicrocksImporter` is unchanged apart from the key; error messages name "port" or
  "service".
- `AddMicrocksBackend` already starts Microcks iff any mock exists — no change.

**Example + CLI**

- If the example gains a mocked port, `OrderFlowDevelopment.cs` must still mirror
  `intropy sys create` output. Likelier: keep the example file-based and cover port mocks in
  tests only, until the CLI can scaffold an HTTP-port template.

## Open question (blocks implementation)

**How does a component talk to an HTTP port at runtime?** Ports are currently file-shaped:
the scaffold sweeps a localstorage binding (`list`/`get`/`delete`; "failed files stay on the
source port"). A Dapr `bindings.http` component exposes `get`/`post`/`put`/`delete` with a
`path` metadata field — a different operation set. Three options:

1. **HTTP output binding with the same name.** Cheapest here, but only works if the
   framework's port abstraction already speaks both binding types (or the component is
   written against HTTP). Needs confirmation in the framework.
2. **Mocked port renders as an `HTTPEndpoint`** and the component reaches it by service
   invocation on the port's name. Reuses the service-mock path exactly, but makes a port look
   like a service to component code — arguably the API is a `Calls` edge, not a port.
3. **Model it as a service, not a port.** If the external API is request/response, declare
   it with `ServiceRef` + `Calls` and mock it today, no changes. Ports stay for
   sweep/drop-shaped transports (file, SFTP, blob).

Recommendation: settle whether an HTTP API is ever a port in Intropy's model before building
anything. If the framework's extractor/loader contract is "sweep items from a binding",
option 3 is the honest answer and this plan reduces to documentation. If the framework
already abstracts the port's transport, option 1 is a small, contained change along the lines
above.

## Tests (if implemented)

- `DevelopmentBuilderTests`: mock for an unused port, double mock, mock + files on one port,
  identity collision between a port mock and a service mock, port resolved by mock alone.
- `GenerationTests`: mocked port emits an HTTP binding with Microcks `url` and `UsedBy`
  scopes; file ports unchanged.
- `MicrocksTests` / readiness: a component waits for its mocked port's gate.
- `EndToEndTests` snapshot: unchanged (model untouched) — a diff there means the design
  leaked into the model.

## Outcome (prototype on `feat/port-mocks`, 2026-10-26)

**Open question answered: option 1.** An HTTP API behind a port is already a port elsewhere in the
platform: `intropy deploy` offers `http` as a port binding, the deploy-component template renders
`bindings.http` for it, and the local k3s cluster ships an `http` fixture. A customer demo
(Uppsala invoicing, a loader POSTing JSON to an invoice API) ran this end to end. The component
invokes the port's binding (`post` with a `path`) through `DaprClient`; `bindings.http` fails a
non-2xx answer, so an API outage stays a technical failure the broker redelivers.

Implemented as sketched, with these specifics:

- `OpenApiMock(Name, ArtifactPath, Title, Version, Target)`: the first field is renamed from
  `AppId` to `Name`, and `Target` is added. Compatibility with `2.1.0` is kept at source and
  binary level: a four-parameter constructor (a service mock), a four-value `Deconstruct`,
  and `AppId` as an `[Obsolete]` alias for `Name`. The change is additive and can ship in a
  minor release (`2.2.0`).
- One `Mocks` list; ordered services first, then by name. The Microcks identity collision
  check spans both kinds.
- `graph --development` keeps `appId` for service mocks and writes `port` for port mocks;
  service-mock output is byte-identical.
- Mocked ports are not restricted to `To` ports. The sweep-based extractor cannot list an HTTP
  binding, but a component polling an API through `From` is legitimate code.
- `MockReadiness` gates per `(target, name)`. A component waits for its mocked ports as well
  as the services it calls, and only those components wait for Microcks.

Verified: 11 new tests (311 total, all green; the `EndToEndTests` model snapshot is unchanged).
Also ran the demo's SystemHost on local packages `2.2.0-portmocks-local.1`, with
`development.Mock(Ports.InvoiceApiLoaderDestination)`: the host imported the contract, the loader
waited for it, and both invoices reached Microcks through the port's own binding.

Not covered here (separate packages):

- **Framework:** an HTTP port adapter, so a Sender does not hand-roll `InvokeBindingAsync`.
- **Templates and CLI:** record the port's shape (files or http) in the scaffold record, so
  `sys create` renders `Mock(port)` and `deploy` offers only bindings of that shape.
