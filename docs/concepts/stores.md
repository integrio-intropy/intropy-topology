# Stores

> A store is storage the system's own components share: the claim store and the archive store. It is minted for the system, like its pubsub, when a component declares that it uses it.

## How it works

Two framework features need storage that is neither a port nor a message channel:

- **The claim check.** A payload too large to travel inside a message is written to the claim store, and a pointer travels instead. The component that reads the message reads the payload back.
- **The archive.** A component copies the payloads it receives to the archive store, so an incident or a trace can show what arrived.

No external system touches either, and neither has a direction, so they are not ports. They are plumbing the system owns, like its pubsub. A component declares that it uses one:

```csharp
builder.AddExtractor("order-extractor")
    .From(Ports.Erp)
    .Publishes(Messages.OrderPlaced)
    .ClaimChecks();

builder.AddLoader("order-loader")
    .Subscribes(Messages.OrderPlaced)
    .To(Ports.Wms)
    .ClaimChecks()      // reads the payloads the extractor claim-checked
    .Archives();
```

- **ClaimChecks()** and **Archives()** are available on extractors, loaders and transactional integrations.
- **One store per role per system**, named after the system: `<system>-claim-store` and `<system>-archive-store`. A store materializes only when a component uses it, and is scoped to the components that do.
- **A subscriber must claim-check too.** `Build()` warns when a component subscribes to a topic that a claim-checking component publishes to without declaring `ClaimChecks()` itself: it could not read a payload kept in the store.
- **Names must not collide.** A store is a Dapr Component, so a port, a pubsub or a cursor store with the same name is a validation error.

## What it renders

Each store becomes one Dapr binding component named after the store. Which storage backs it (a blob container, a share) is deployment configuration, as a port's binding type is.

Locally a store is a `bindings.localstorage` folder, `test/<store>` under the system host, which the host creates. The host points each component at the stores it uses with the settings the framework reads a store from:

| Component declares | Variables the host sets |
|---|---|
| `ClaimChecks()` | `ClaimStore__DaprBindingName=<system>-claim-store`, `ClaimStore__Kind=Local` |
| `Archives()` | `ArchiveStore__DaprBindingName=<system>-archive-store`, `ArchiveStore__Kind=Local` |

A deployment sets the same variables for the binding it renders. Whether and when a component actually claim-checks or archives stays the component's own configuration (its claim check mode and threshold, `Archive:Mode`).

## Materialized stores

```csharp
public sealed record StoreResource
{
    public required string Name { get; init; }          // <system>-claim-store / <system>-archive-store
    public required StoreRole Role { get; init; }       // Claim or Archive
    public required IReadOnlyList<string> UsedBy { get; init; }
}
```

`SystemTopology.Stores` is omitted from the serialized model when no component uses a store, and so are a component's `ClaimStore` and `ArchiveStore`: systems without stores serialize unchanged. The `graph` verb lists the stores under `stores` (`name`, `role`, `usedBy`) and names them on each component as `claimStore` and `archiveStore`.

## Related

- [Ports](ports.md): the system's edges, which a store deliberately is not
- [Components](components.md)
