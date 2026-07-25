# Follow-up task: Binding → Connector redefinition

> Status: implemented

**Prerequisite: `REFACTORING.md` — verified complete** (staged builders in `BlockBuilders.cs`
over `ComponentCore`, Workload→Component rename, trimmed rules, `[ConstantExpected]`,
`InputBinding`/`SubscribedTopic` property names). This task may start.

Additional notes against the current code:

- Trigger property rename is now `InputBinding` → `InputConnector` (not `Binding` →).
- With derived component names (`binding.<connector-name>`), `PubSubBindingNameCollisionRule`
  becomes nearly moot (prefix conventions prevent accidental collision). Evaluate: keep as a
  cheap safety net or retire its ITP code properly — do not leave it half-adapted.

**Background.** We are redefining the domain term *Connector*: it is no longer "a reusable
adapter library that plugs into a block" but **the named connection point between the landscape
and an external system**. Every edge block reaches the outside world through a connector. A
connector has a *transport* — the kind of Dapr binding it materializes as. All connectivity
runs through Dapr (HTTP and SFTP included), so every connector becomes exactly one Dapr
binding component; the transport selects its `spec.type`, and the component name is derived
(`binding.<connector-name>`), never declared. The win is domain-level: the model names the
connection point and makes its kind explicit instead of leaving both buried in Dapr YAML.
This decision is deliberate and provisional; if the terminology doesn't land with the team we
will revise again.

## 0. Preliminary rename (own commit, before the connector work)

"Block" is implementation-model vocabulary (the pattern catalog); the topology declares
runtime-model *Components*, and the code already uses Component everywhere else
(`ComponentKind`, `ComponentModel`, `ComponentCore`). Remove the vocabulary mix:

- `BlockBuilder<TSelf>` → `ComponentBuilder<TSelf>`;
  `PublishingBlockBuilder<TSelf>` → `PublishingComponentBuilder<TSelf>`;
  `BlockBuilders.cs` → `ComponentBuilders.cs`.
- Let `ContainerServiceBuilder` inherit `ComponentBuilder<ContainerServiceBuilder>` and delete
  its duplicated `Uses` method.
- XML docs may keep "edge block"/"core block" when explaining *pattern* semantics — block is
  the pattern a component implements, not what the entity is.

## 1. New types

```csharp
/// A named connection point to an external system. Declared as static fields in a
/// scaffolded Connectors.cs; system-owned, never shared across systems.
public sealed record ConnectorRef
{
    public string Name { get; }                  // DNS-1123 label, e.g. "pim"
    public ExternalSystem System { get; }
    public Transport Transport { get; }

    public static ConnectorRef Define(
        [ConstantExpected] string name, ExternalSystem system, Transport transport);
}

/// The kind of Dapr binding a connector materializes as. All connectivity goes through
/// Dapr; the transport selects the binding type (spec.type) — it never bypasses Dapr.
/// The Dapr component name is always derived: binding.<connector-name>.
public abstract record Transport
{
    public static Transport Http();    // bindings.http (output-only in Dapr)
    public static Transport Sftp();    // bindings.sftp
    public static Transport Blob();    // bindings.azure.blobstorage
    public static Transport Custom([ConstantExpected] string daprType);  // escape hatch

    public abstract bool SupportsInput { get; }    // used by capability validation
    public abstract bool SupportsOutput { get; }
}
```

Usage (this replaces the current `Bindings` class in README and tests):

```csharp
public static class Connectors
{
    public static readonly ConnectorRef Pim =
        ConnectorRef.Define("pim", new ExternalSystem("pim"), Transport.Sftp());  // → binding.pim
    public static readonly ConnectorRef Erp =
        ConnectorRef.Define("erp", new ExternalSystem("erp"), Transport.Http());  // → binding.erp
}
```

## 2. Renames and API changes

| Current | New |
|---|---|
| `BindingRef` | `ConnectorRef` (shape changes per §1 — not a pure rename) |
| `.FromBinding(bindingRef)` | `.From(connectorRef)` |
| `.ToBinding(bindingRef)` | `.To(connectorRef)` |
| `Trigger.Binding(bindingRef)` / `BindingTrigger` | `Trigger.Connector(connectorRef)` / `ConnectorTrigger` (property: `InputConnector`) |
| `BindingResource` | `ConnectorResource` — gains `Transport` and derived `DaprComponentName` (`binding.<name>`); keeps `ExternalSystemName`, `Directions`, `UsedBy` |
| `BindingDirection` | keep name `Direction` or `ConnectorDirection` (In/Out semantics unchanged) |
| `TriggerKind.Binding` | `TriggerKind.Connector` |
| Rules: `BindingExternalSystemConflictRule`, `PubSubBindingNameCollisionRule`, etc. | rename to connector terms; **keep their ITP codes** |

Grammar (staged builders) is unchanged in structure: `From`/`To` and `Trigger.Connector`
are legal exactly where `FromBinding`/`ToBinding`/`Trigger.Binding` were.

## 3. New validation rules

- Capability rules (new ITP codes, Error severity): `.From(c)` and `Trigger.Connector(c)`
  require `c.Transport.SupportsInput`; `.To(c)` requires `c.Transport.SupportsOutput`
  (e.g. Dapr's HTTP binding is output-only).
- Existing "binding external-system conflict" rule becomes: same connector name declared with
  different external systems or transports → Error. Derived Dapr component names can only
  collide when connector names collide, so no separate name-collision rule is needed.

## 4. Materialization

`ConnectorResource` entries are deduplicated by connector name (as bindings are today by
component name). Deterministic ordering by name. `SystemTopology.Bindings` →
`SystemTopology.Connectors`.

## 5. Docs

- README: update example and prose. State the definition in one line: *"A Connector is the
  named connection point between the system and an external system; its transport selects the
  Dapr binding type it materializes as (`binding.<name>`, `spec.type` from the transport)."*
- XML docs: never describe a connector as an adapter/library/plugin. The Dapr resource is
  always fully qualified as "Dapr binding component".

## 6. Acceptance criteria

1. Build clean, all tests green; snapshot test updated in the same commit as the model rename
   (property renames expected; values unchanged except `Transport` additions).
2. An HTTP-transport connector round-trips: appears in `SystemTopology.Connectors` with
   `Transport = Http` and derived `DaprComponentName = "binding.<name>"`.
3. Capability violations fail Build with the new ITP codes: `.From`/`Trigger.Connector` with
   an output-only transport (Http), and `.To` with an input-only transport.
4. `grep -ri "FromBinding\|ToBinding\|BindingRef" src test README.md` returns nothing.
