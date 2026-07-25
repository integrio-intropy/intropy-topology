# Intropy.Topology.Aspire

The .NET Aspire **run-backend** for [`Intropy.Topology`](https://www.nuget.org/packages/Intropy.Topology).
Turns a validated `SystemTopology` into a live, F5-able Aspire application — with Dapr.

The adapter is a one-way **translator**. At AppHost startup it:

1. **discovers** the system's validated topology from the assembly (`SystemDiscovery`),
2. **generates** dev Dapr components in-memory (`Intropy.Topology.Generation`) to a temp folder, then
3. **translates** the model into Aspire resources — a plain RabbitMQ container behind pub/sub,
   `AddContainer` for declared mock external systems, one Microcks instance serving every
   contract-backed mock, one `AddProject` (by folder convention) or dev-override container per
   component, each with a Dapr sidecar (`app-id` = component name) whose `--resources-path`
   points at the generated dev YAML — and runs it under DCP.

Aspire never sees the topology; it receives ordinary resources. This mirrors the one-way
relationship Generation has with Dapr YAML, targeting a live object model instead of files.

Components that declare a schedule (`WithSchedule`, [ADR 0010](../../docs/decisions/0010-schedule-as-activation-attribute.md))
are re-run on their cron ticks by the `ComponentScheduler` — CronJob emulation for the dev
loop: sidecar restarted first, overlapping ticks skipped with a warning, a "Run now"
command on the dashboard, local time zone. Cronos (cron next-occurrence math) is this
package's only dependency beyond Aspire itself.

## Usage

One entry point in the AppHost, two backends of the same discovered truth:

```csharp
using System.Reflection;
using Intropy.Topology.Aspire;
using Intropy.Topology.Generation;

var assembly = Assembly.GetExecutingAssembly();
return args is ["run", ..] or []
    ? await IntropyAspire.RunAsync(assembly, args)     // → Aspire + DCP
    : await IntropyGenerate.RunAsync(assembly, args);  // → generate | check | graph
```

Each component is resolved to the sibling project that runs it by folder convention
(`order-extractor` → `../order-extractor/order-extractor.csproj`). A component can instead be mapped
to a container image, and external systems can be mocked, via the `IDevProfile` seam on the system
definition:

```csharp
public sealed class OrderFulfillmentSystem : ISystemDefinition, IDevProfile
{
    public string SystemName => "order-fulfillment";
    public void Define(SystemBuilder builder) { /* ... topology ... */ }

    public void ConfigureDev(DevBuilder dev)
    {
        dev.MockExternalSystem("webshop", "atmoz/sftp:latest", 2222);
        dev.Override("erp-order-loader", "myregistry/erp-loader:1.0"); // run as a container, not a project

        // Contract-backed mocks, served by one Microcks instance (docs/decisions/0006):
        dev.MockHttpExternalSystem("erp", "mocks/erp-orders-openapi.yaml", "erp-orders", "1.0.0");
        dev.MockApi("freight-booking", "mocks/freight-api-openapi.yaml", "freight-booking-api", "1.0.0");
    }
}
```

`MockHttpExternalSystem` points a `bindings.http` connector at a REST mock generated from the
OpenAPI contract. `MockApi` mocks an *internal* API: its provider component is not started, and a
generated Dapr `HTTPEndpoint` resource routes service invocation of the provider's app-id to the
mock — so a single component can be debugged without running its siblings. Artifact paths resolve
against the AppHost project directory; `serviceName`/`serviceVersion` must match the contract's
`info.title`/`info.version`.

Dev declarations never enter the `SystemTopology`, so the prod model stays pure.

## Prerequisites for `run`

- The [.NET Aspire](https://aspire.dev) tooling and an `Aspire.AppHost.Sdk` AppHost project.
- Docker and [Dapr](https://docs.dapr.io) initialized locally (`dapr init`).

## Notes

- Dev pub/sub is backed by RabbitMQ on a fixed host port (5672), and contract-backed mocks by Microcks
  on 8585; the generated YAML points at them. This resolves the generate-before-run ordering; a port
  conflict there is a known limitation.
- Synchronous APIs (`Provides`/`Consumes`) create no Dapr component — Dapr service invocation
  addresses providers by app-id, which the sidecars already establish. A `MockApi` declaration is
  the exception: it emits an `HTTPEndpoint` resource named after the (skipped) provider.
- Topic mocking is out of scope, though no longer for protocol reasons: Microcks' async minion
  speaks AMQP, which the RabbitMQ dev broker uses (ADR 0006, amended).
- The runtime-config contract injected into each project (`INTROPY__COMPONENT`, `INTROPY__CONFIG`)
  is provisional until the Intropy Hosting framework consumes it.
