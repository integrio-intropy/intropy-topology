# Intropy.Topology.Aspire

The .NET Aspire **run backend** for [`Intropy.Topology`](https://www.nuget.org/packages/Intropy.Topology).

It turns a validated `SystemTopology` into a local, F5-able Aspire application with Dapr. At AppHost startup it:

1. discovers the validated system definition from the supplied assembly;
2. generates Dapr components and per-component runtime configuration into a temporary directory; and
3. translates the topology to Aspire resources: one Redis container for pub/sub and one sibling .NET project plus Dapr sidecar for each topology component.

The adapter is deliberately one-way: Aspire receives ordinary resources; it does not need to understand the topology model.

## Usage

One SystemHost entry point selects the run or generation backend from the same discovered system definition:

```csharp
using System.Reflection;
using Intropy.Topology.Aspire;
using Intropy.Topology.Generation;

var assembly = Assembly.GetExecutingAssembly();
return args is ["run", ..] or []
    ? await IntropyAspire.RunAsync(assembly, args)     // Aspire + DCP
    : await IntropyGenerate.RunAsync(assembly, args);  // generate | check | graph
```

## Project convention

Each topology component is resolved to a sibling project relative to the SystemHost:

- `order-extractor` resolves to `../order-extractor/src/*.csproj` when `src` contains exactly one project;
- otherwise it resolves to `../order-extractor/order-extractor.csproj`.

A component's standalone Dapr resources live at `../<component>/local/dapr-components/`. For Aspire runs, they are staged with generated resources at the stable, inspectable path `obj/dapr-components/<component>/`; generated resources replace local resources with the same Dapr `metadata.name`.

## Runtime behavior

- Redis backs development pub/sub. It is published at host port `6380` because generated Dapr resources target `localhost:6380`; `dapr init` commonly reserves Redis's default `6379`.
- Components receive `INTROPY__COMPONENT` and `INTROPY__CONFIG` environment variables. The latter points to that component's generated `.intropy.json` file.
- A publisher waits for its subscribers during startup when the topic graph is acyclic. Dapr sidecars also wait for Redis readiness before they start.
- An extractor declared with `WithSchedule` runs once at AppHost startup and is re-run on its cron ticks. The dashboard provides a **Run now** command; overlapping runs are skipped.

## Prerequisites

- An AppHost project using the .NET Aspire SDK.
- Docker.
- The Dapr CLI initialized locally: `dapr init`.

## Scope

This package implements the repository's minimal topology model. Development overrides, external-system mocks, Microcks, API mocking, and additional transport/runtime backends remain on the `full-topology` branch; they are not supported here.
