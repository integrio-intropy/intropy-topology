# Plan: `graph --development` + move local file paths into dev settings

## Goal

1. Add a `--development` flag to the `graph` verb that includes a `development` section in the
   serialized `topology.intropy.io/v1` document.
2. Move local file read/write paths out of the topology declaration (`Transport.File(rootPath)`)
   into `IDevelopmentDefinition`, so the development section is the complete local-run picture:
   service mocks **and** connector file resolutions.

Backwards compatibility is explicitly not a concern (pre-launch).

## Design decisions (agreed)

- **`Transport.File(rootPath)` is removed.** Local folders are not edge properties.
- **`ConnectorRef` becomes deployed-transport-first:**
  `ConnectorRef.Define("erp", Transport.Sftp())` declares the edge with its deployed shape.
  `WithDeployed` and `ConnectorResource.DeployedTransport` are removed.
  `FileTransport` is removed from the core `Transport` hierarchy; the file root path becomes
  development data, not a `Transport`.
- **Unresolved dev connector fails at development discovery** with a focused
  `DevelopmentValidationException`, not at `Build()`. No silent `./test/<name>` default.
- **CLI codegen** (`intropy sys create`) scaffolds a `Development.cs` next to `Connectors.cs`,
  writing the `./test/<connector-name>` convention explicitly into the template. The convention
  lives in user code, not in the framework. (CLI change tracked separately in intropy-cli; this
  repo's example SystemHost mirrors the new golden output.)

## Target serialized shape (`graph --development`)

```json
{
  "apiVersion": "topology.intropy.io/v1",
  "kind": "SystemTopology",
  "system": "order-flow",
  "components": [ ... ],
  "topics": [ ... ],
  "connectors": [
    {
      "name": "order-extractor-source",
      "transport": { "type": "sftp", "supportsInput": true, "supportsOutput": true },
      "directions": [ "in" ],
      "usedBy": [ "order-extractor" ]
    }
  ],
  "services": [ ... ],
  "development": {
    "mocks": [
      {
        "appId": "idempotency-service",
        "artifact": "mocks/idempotency-service.openapi.yaml",
        "title": "Idempotency Service",
        "version": "1.0.0",
        "baseUri": "http://localhost:8585/rest/Idempotency%20Service/1.0.0"
      }
    ],
    "connectors": [
      { "connector": "order-extractor-source", "transport": "file", "rootPath": "./test/order-extractor-source" }
    ]
  }
}
```

- `development` is an **object** (room to grow: future dev-time knobs), omitted entirely without
  the flag (`WhenWritingNull` already in place — output without the flag is unchanged except for
  the connector transport simplification).
- Connector `transport` no longer has a `deployedTransport` sibling; the single declared
  transport is the deployed shape.
- `rootPath` is reported **as declared** (relative), not absolutized — absolutization remains a
  generation-time concern.

## Implementation steps

### 1. Core model (`src/Intropy.Topology`)

- `Refs.cs`:
  - Remove `FileTransport` and `Transport.File(rootPath)`.
  - Remove the `"file"` `[JsonDerivedType]`.
  - `ConnectorRef.Define(name, Transport transport)` — keep signature, doc-comment rewording:
    the transport is now the *deployed* shape.
  - Remove `ConnectorRef.WithDeployed` and `DeployedTransport`.
  - Keep `SftpTransport` (value-free) as the only built-in transport for now.
- `Model/Resources.cs`: remove `ConnectorResource.DeployedTransport`.
- `Validation/Rules/TopicAndConnectorRules.cs`:
  - `ConnectorConflictRule`: simplify — conflict is now "same connector name, different
    deployed transport" (the `Local`/`Deployed` tuple collapses to the single transport).
  - `DeployedTransportCapabilityRule`: rename concept to `ConnectorTransportCapabilityRule` —
    capability check now applies to the single declared transport.
- Materializer (`Building/`): drop `DeployedTransport` plumbing.

### 2. Development model (`src/Intropy.Topology.Generation/Development.cs`)

- `DevelopmentBuilder` gains connector file resolutions:

  ```csharp
  public FileResolutionBuilder Files(ConnectorRef connector) // validates: connector is used by topology, not already resolved
  // usage: development.Files(Connectors.Erp).RootPath("./test/erp");
  ```

- `DevelopmentManifest` becomes `(IReadOnlyList<OpenApiMock> Mocks, IReadOnlyList<FileResolution> Connectors)`
  where `FileResolution = record(string ConnectorName, string RootPath)`.
- Validation at `Build()`:
  - every connector used by the topology **must** have a resolution → else
    `DevelopmentValidationException` naming the unresolved connectors.
  - root path must stay inside the SystemHost directory (reuse the escape-check pattern from
    `OpenApiArtifact.Inspect`, including symlink resolution).
  - duplicate resolution for the same connector → `DevelopmentValidationException`.
- `DevelopmentDiscovery` unchanged in shape; it just produces the richer manifest.

### 3. Generation (`Generation.cs`)

- `TopologyGenerator.Generate` reads the root path from
  `development.Connectors.Single(c => c.ConnectorName == connector.Name)` instead of
  `connector.Transport is FileTransport`; absolutization (`Path.GetFullPath`) stays here.
- Binding `spec.type` for a file-resolved connector is `bindings.localstorage` — the resolution
  now supplies the local binding type, so map `FileResolution` → localstorage metadata.
- The parameterless `Generate(topology)` overload keeps working: it means "no dev substitutions",
  which now implies no binding components can be emitted for connectors... — **decision:** drop
  the parameterless overload; generation always takes a manifest (empty manifest = error only if
  the topology has connectors, surfaced as a clear exception). Aspire backend already discovers
  the manifest, so it just consumes the new shape.

### 4. The `graph --development` flag (`IntropyGenerate.cs`)

- `RunAsync`: pass `args` to `Graph(assembly, args)`.
- `Graph`: `var includeDevelopment = args.Contains("--development");` — when set, call
  `DevelopmentDiscovery.Discover(assembly, topology, Directory.GetCurrentDirectory())` and add
  the section. (Error behavior comes free: existing broad catch prints `error: …`, exit 1.)
- New records: `GraphDevelopment(IReadOnlyList<GraphMock>? Mocks, IReadOnlyList<GraphFileConnector>? Connectors)`.
- `GraphConnector`: drop `DeployedTransport`; single `Transport`.
- Unknown flags on `graph` → keep ignoring (consistent with `generate`'s lax parsing today);
  note in help text.

### 5. Example SystemHost (`examples/OrderFlow.SystemHost`)

- `Connectors.cs`: `ConnectorRef.Define("order-extractor-source", Transport.Sftp())` etc.
- `OrderFlowDevelopment.cs`: add
  `development.Files(Connectors.OrderExtractorSource).RootPath("./test/order-extractor-source");`
  and the loader destination likewise.
- These files mirror `intropy sys create` golden output — **flag in the PR description** that
  intropy-cli templates + `create_test.go` goldens must be updated in lockstep.

### 6. Tests

- `Intropy.Topology.Test`:
  - Update `RefTests`, `MaterializationTests`, `RuleTests`, `TestContracts`, `EndToEndTests`
    (incl. the **byte-exact JSON snapshot** — model change requires snapshot regeneration).
  - New rule tests: connector-transport capability on the single transport.
- `Intropy.Topology.Generation.Test`:
  - New `DevelopmentDiscovery`/`DevelopmentBuilder` tests: unresolved connector error, duplicate
    resolution, path escape (incl. symlink), happy-path manifest content.
  - `TopologyGeneratorTests`: rootPath now sourced from manifest; unresolved → exception.
  - **New `graph --development` tests**: section present with flag, absent without; mock +
    file-resolution content; no-dev-definition + flag → section omitted (or empty — pick
    omitted for cleanliness).
- `Intropy.Topology.Aspire.Test`: update to new manifest/transport shape.
- `dotnet test Intropy.Topology.slnx` green before PR.

## Out of scope (noted for follow-up)

- intropy-cli template + golden updates (`internal/system/create_test.go`) — separate repo/PR,
  must land together.
- `SftpTransport` gaining address/path etc. — remains deployment-owned configuration.
- Help/usage text for the verbs (no `--help` exists today).

## Risks

- **Codegen drift**: the example SystemHost is a compile-time guarantee for CLI output; merging
  this without the CLI PR leaves the guarantee stale. Mitigation: link both PRs, merge in order.
- **Snapshot churn**: the byte-exact snapshot in `EndToEndTests` will change; review the diff
  carefully to confirm only intended model changes appear.
