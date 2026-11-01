---
title: Custom Network RPC
---

# Custom network RPC

Use TSMP RPC for momentary events such as a bell sound or an animation trigger. Store persistent values such as a door's `isOpen` or the current game phase in `[TransSync]` fields so periodic state refresh can restore them after lost frames.

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
public bool SendTransRPC(string methodName, RPCTarget target)
```

| Parameter | Meaning |
| --- | --- |
| `methodName` | Public method name to execute on the target behaviour. Use `nameof(Method)` when possible. |
| `target` | `Local`, `Remote`, or `All`. |

For `Remote` and `All`, `true` means the local encoder accepted the event into its queue. It does not acknowledge receipt or execution by another client. `false` can mean an unassigned encoder, a full queue, or an event that cannot fit the current payload budget.

`All` executes the local method before trying to queue the remote event. The local side effect may already have happened when the return value is `false`; retrying the whole `All` call can execute it again.

## `RPCTarget`

| Target | Sender behaviour | Receiver behaviour |
| --- | --- | --- |
| `Local` | Executes method immediately. | Nothing is queued. |
| `Remote` | Does not execute locally. | Queues method for decoded receivers. |
| `All` | Executes immediately. | Also queues method for decoded receivers. |

If sender and receiver are looped back through OBS or another capture path, `All` can run once immediately and again after the capture delay.

## Reliability and repeat frames

The encoder publishes each queued event for `transRpcRepeatFrames` successful output frames, four by default. The receiver suppresses repeated positive event IDs within its bounded deduplication cache. These copies have no acknowledgement: a burst of missing video can cover every copy and lose the event permanently. Periodic state refresh does not recreate RPCs.

Increasing repeats can help with isolated frame loss, but also keeps the head event in the FIFO queue longer and delays later events. Check queue growth and first-application delay before increasing it. Use state fields for information that must eventually reflect the sender's current value; they do not preserve every intermediate transition.

## Method restrictions

- The method must exist on the receiving `TSMPNetworkBehaviour`.
- Keep RPC methods public and argument-free for the Udon-safe path.
- Put data in `[TransSync]` fields when the event needs parameters.
- Keep side effects idempotent when repeated frames are possible.

## Debug checklist

| Symptom | Check |
| --- | --- |
| Local action works but delayed action does not | Encoder `queuedRpcCount`, `rpcMessageCount`, and decoder `lastRpcCallCount`. |
| Event is missed | Check queue acceptance, payload deferral and video loss. All finite copies can be lost; choose a state field for persistent values. |
| Events arrive late under load | Check queue growth and repeat count before increasing retransmissions. |
| Wrong object receives event | Run `Apply Setup` and confirm `networkId` values are unique. |
