# Services

> A shared platform service is a container the whole system depends on — declared on the system, started by the run backend, and ready before any component starts.

## Declaring a service

Services are declared as static fields in a scaffolded `Services.cs` and registered on the system with `UsesService`:

```csharp
public static class Services
{
    public static readonly ServiceRef Idempotency =
        ServiceRef.Define("idempotency", Service.Container("docker.io/library/redis:7", 6379));
}

// in ISystemDefinition.Define
builder.UsesService(Services.Idempotency);
```

Unlike topics and connectors, services do **not** materialize from usage — no edge references them, so the declaration is explicit. They are system-level: there is no per-component `.Uses`, and every component waits for every declared service ([ADR 0011](../decisions/0011-system-level-platform-services.md)).

## The contract

Starting the service and the startup ordering **is the whole contract**:

- The Aspire run backend starts the container and holds every component and Dapr sidecar until the service answers on its port.
- No connectivity is injected — no environment variables, no connection strings. Components reach the service on the fixed port the declaration states.
- The declared port is both the container port and the host port (generation runs before the system starts, so endpoints must be known up front — the same constraint that fixes RabbitMQ's port).

`Service.Container(image, port)` takes a plain Docker image reference; the tag is part of the image string (`redis:7`), and digest references are unsupported. Service names are DNS-1123 labels; `rabbitmq` is reserved for the run backend's broker (ITP402).

## Validation

| Code | Meaning |
|------|---------|
| ITP108 | One service name declared with two different definitions |
| ITP402 | A service name collides with a component name or the reserved `rabbitmq` |

A system declaring only services is still empty (ITP002) — services declare no integration.

## Related

- [Materialization](materialization.md) — where `ServiceResource` lands in the model
- [Validation](validation.md) — the full diagnostic code table
