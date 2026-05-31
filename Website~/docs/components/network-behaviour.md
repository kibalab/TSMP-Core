---
title: TSMPNetworkBehaviour
---

# TSMPNetworkBehaviour

`TSMPNetworkBehaviour` is the base component for data that TSMP can send or receive.

Most users interact with the ready-made TSMP network components listed in [Sync components](./sync-components.md). You only need to understand the shared settings that appear on each component.

## Network ID

Each synchronized component has a network ID. Sender and receiver use this ID to match messages to objects.

Use `TSMPSetup` to assign and refresh IDs. Edit IDs manually only when you need a fixed mapping.

Rules:

- IDs must be unique among active TSMP network behaviours that share a stream.
- Sender and receiver must agree on IDs.
- Re-run `Apply Setup` after adding, deleting, or moving components.
- If two behaviours share the same network ID, the receiver may apply data to the wrong object or ignore messages.

Treat manual IDs as an advanced option. The Inspector locks the field by default to prevent accidental changes.

## Receive interpolation

Set receive interpolation per component:

| Mode | Use when |
| --- | --- |
| None | This object should not apply received values. Useful for sender-only objects. |
| Discrete | The value should snap to the newest received state. Good for events, toggles, animator state, and low-rate values. |
| Continuous | The value should move smoothly between received states. Good for transforms and pose targets that support smoothing. |

`None` does not stop the component from sending. It only tells the receiver side to ignore incoming values on that component.

## Active state

TSMP uses component and GameObject active state as the main on/off switch.

- Disable the component to stop that behaviour from participating.
- Disable the GameObject to stop all TSMP behaviours on it.
- Re-run setup if you add, remove, or permanently reorganize synced objects.

Temporary runtime enable/disable state is respected during encode and receive. Permanent scene structure changes should be followed by `Apply Setup`.

## Sending an RPC

Use `SendTransRPC` from a `TSMPNetworkBehaviour`:

```csharp
SendTransRPC(nameof(ToggleObject), RPCTarget.All);
```

Targets:

| Target | What happens |
| --- | --- |
| `Local` | Only the caller runs the method. Nothing is sent through TSMP. |
| `Remote` | Only receivers run the method after the stream delay. |
| `All` | Caller runs now, receivers run after the stream delay. |

For a minimal example, use `TSMPNetworkGameObjectToggle`.

## When receiver objects do not update

Check:

1. The receiver component is enabled and active.
2. Receive interpolation is not `None`.
3. The network ID matches the sender.
4. `TSMPSetup` was applied after the component was added.
5. Decoder message count is greater than zero.
