# Intropy.Topology.Generation

Generation for [`Intropy.Topology`](https://www.nuget.org/packages/Intropy.Topology): turns a
validated `SystemTopology` into Dapr components and per-component runtime config for the local run
profile. Backend-agnostic and dependency-free (YAML is emitted by a small internal writer).

It produces, deterministically:

- one Dapr **pub/sub** component per distinct `PubSubName` (Redis Streams backed),
- one Dapr **binding** per connector (`bindings.localstorage` rooted at the connector's
  development-manifest folder, `scopes` from `UsedBy`),
- one Dapr **HTTPEndpoint** per development-mocked platform service, named after the service's
  app-id so service invocation resolves to the mock,
- a per-component `*.intropy.json` describing that component's identity and edges.

```csharp
var discovered = SystemDiscovery.Discover(assembly);
var development = DevelopmentDiscovery.Discover(assembly, discovered.Topology, root);
var artifacts = TopologyGenerator.Generate(discovered.Topology, development);
artifacts.WriteTo("./out");
```

It also hosts the non-Aspire CLI verbs over a discovered system:

```csharp
await IntropyGenerate.RunAsync(assembly, ["check"]);              // validate
await IntropyGenerate.RunAsync(assembly, ["graph"]);             // print topology.intropy.io/v1 JSON only on stdout
await IntropyGenerate.RunAsync(assembly, ["generate", "./out"]); // write artifacts
```

The Aspire run-backend (`Intropy.Topology.Aspire`) uses this same generator in-memory, so F5 and
`generate` emit the same truth.
