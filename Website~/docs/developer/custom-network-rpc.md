---
title: Custom Network RPC
---

# Custom network RPC

Use TSMP RPC when you need an event to travel through the texture stream. It is useful for delayed interactions, toggles, triggers, and one-shot state transitions.

TSMP RPC does not inject method calls automatically. Call `SendTransRPC(methodName, target)` from your `TSMPNetworkBehaviour`.

## Minimal RPC component

```csharp
using K13A.TSMP.Udon;
using UnityEngine;

public class BellSync : TSMPNetworkBehaviour
{
    public AudioSource bell;

    public override void Interact()
    {
        SendTransRPC(nameof(Ring), RPCTarget.All);
    }

    public void Ring()
    {
        if (bell != null)
        {
            bell.Play();
        }
    }
}
```

## `SendTransRPC()`

```csharp
public void SendTransRPC(string methodName, RPCTarget target)
```

| Parameter | Meaning |
| --- | --- |
| `methodName` | Public method name to execute on the target behaviour. Use `nameof(Method)` when possible. |
| `target` | `Local`, `Remote`, or `All`. |

## `RPCTarget`

| Target | Sender behaviour | Receiver behaviour |
| --- | --- | --- |
| `Local` | Executes method immediately. | Nothing is queued. |
| `Remote` | Does not execute locally. | Queues method for decoded receivers. |
| `All` | Executes immediately. | Also queues method for decoded receivers. |

If sender and receiver are looped back through OBS or another capture path, `All` can run once immediately and again after the capture delay.

## Reliability and repeat frames

RPCs are visible for `transRpcRepeatFrames` on the encoder. Increase this when:

- The capture path drops frames.
- OBS or Spout produces irregular frame pacing.
- Interactions are being missed.

Do not use very high repeat counts for rapidly repeated events. A receiver may see the same event longer than expected.

## Method restrictions

- The method must exist on the receiving `TSMPNetworkBehaviour`.
- Keep RPC methods public and argument-free for the Udon-safe path.
- Put data in `[TransSync]` fields when the event needs parameters.
- Keep side effects idempotent when repeated frames are possible.

## Debug checklist

| Symptom | Check |
| --- | --- |
| Local action works but delayed action does not | Encoder `queuedRpcCount`, `rpcMessageCount`, and decoder `lastRpcCallCount`. |
| Event is missed | Increase `transRpcRepeatFrames` and verify frame loss in the debug canvas. |
| Wrong object receives event | Run `Apply Setup` and confirm `networkId` values are unique. |
