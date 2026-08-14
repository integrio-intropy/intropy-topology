# Cross-repo contract schemas

Machine-checked JSON Schema documents for the contracts that cross the
`intropy-topology` / `intropy-templates` / `intropy-cli` boundary. Owned as
decided in [`docs/decisions/0012`](../docs/decisions/0012-host-the-cross-repo-schemas.md).

| Schema | Contract | Author |
|--------|----------|--------|
| `topology-graph.schema.json` | The `topology.intropy.io/v1` record the `graph` verb prints | this repo |
| `scaffold-record.schema.json` | `.intropy/scaffold.json` | `intropy-cli` (hosted here) |
| `block-kinds.schema.json` | Canonical block-kind spellings | this repo |
| `port-bindings.schema.json` | Canonical port binding / fixture kinds | this repo |

## Rules

- **Additive-only.** Every level allows additional properties. A forward-compatible
  emitter that adds a field still validates; breaking changes (rename, retype, remove)
  require an `apiVersion` bump.
- **The schema describes the wire, not the type.** `topology-graph.schema.json` is
  validated against the `graph` verb's actual stdout in
  `test/Intropy.Topology.Generation.Test/GraphDocumentSchemaConformance.cs`.
- **Validate at authoring / test time, never at runtime.** Consumers vendor a pinned copy.

## Consuming

Pin a release tag and vendor the file(s) you need into your repo:

```sh
TAG=v1.2.0
curl -fsSL "https://raw.githubusercontent.com/integrio-intropy/intropy-topology/${TAG}/schemas/topology-graph.schema.json" \
  -o internal/topology/schemas/topology-graph.schema.json
```

- **intropy-cli** vendors into `internal/topology/schemas/` and validates with
  `github.com/santhosh-tekuri/jsonschema` in tests.
- **intropy-templates** validates in CI against the pinned raw files (no Go toolchain —
  a Python JSON Schema validator).

Do not fetch `main` or `@latest`: a new topology release must not break a consumer's
build. Bump the pinned tag deliberately.
