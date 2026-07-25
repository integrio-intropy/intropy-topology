# Intropy.Topology.Generation

Generation for [`Intropy.Topology`](https://www.nuget.org/packages/Intropy.Topology): turns a
validated `SystemTopology` into Dapr components and per-component runtime config, for a `Dev` or
`Prod` profile. Backend-agnostic and dependency-free (YAML is emitted by a small internal writer).

It produces, deterministically:

- one Dapr **pub/sub** component per distinct `PubSubName` (dev: RabbitMQ-backed),
- one Dapr **binding** per connector (`type` from `Transport.DaprType`, `scopes` from `UsedBy`);
  in dev, `bindings.http` connectors with a declared `MockHttpExternalSystem` point at the
  contract-backed mock server (fixed port 8585, see `docs/decisions/0006`),
- one Dapr **HTTPEndpoint** per dev-mocked internal API (`MockApi`), named after the provider's
  app-id so service invocation resolves to the mock (dev profile only),
- a per-component `*.intropy.json` describing that component's identity and edges.

```csharp
var discovered = SystemDiscovery.Discover(assembly);
var dev = DevManifest.From(discovered.Definition);
var artifacts = TopologyGenerator.Generate(discovered.Topology, GenerationProfile.Dev, dev);
artifacts.WriteTo("./out");
```

It also hosts the non-Aspire CLI verbs over a discovered system:

```csharp
await IntropyGenerate.RunAsync(assembly, ["check"]);              // validate
await IntropyGenerate.RunAsync(assembly, ["graph"]);             // print SystemTopology JSON
await IntropyGenerate.RunAsync(assembly, ["generate", "./out"]); // write artifacts
```

The Aspire run-backend (`Intropy.Topology.Aspire`) uses this same generator in-memory, so F5 and
`generate` emit the same truth.
