---
title: Custom Network Behaviour
---

# Custom network behaviour

Create a custom network component when built-in sync components do not cover the data you need.

Custom components should inherit from `K13A.TSMP.Udon.TSMPNetworkBehaviour`. This gives the component a network ID, receive interpolation setting, TSMP RPC helpers, and encode/receive lifecycle hooks.

## Basic shape

```csharp
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public class ExampleCounterSync : TSMPNetworkBehaviour
{
    public int localCounter;

    [TransSync("counter.value")]
    public int syncedCounter;

    public override void TSMPBeforeEncode()
    {
        syncedCounter = localCounter;
    }

    public override void OnTSMPVariableReceived()
    {
        base.OnTSMPVariableReceived();

        if (receiveInterpolation == ReceiveInterpolationMode.None)
            return;

        localCounter = syncedCounter;
    }
}
```

The `[TransSync]` key should be stable. Changing it changes the variable hash, so existing sender/receiver bindings no longer match.

## Encoding lifecycle

The encoder calls `TSMPBeforeEncode()` before reading `[TransSync]` fields.

Use this to:

- Copy scene state into sync fields.
- Pack multiple values into one `byte[]`.
- Avoid work when `IsTSMPActive()` is false.
- Keep expensive calculations outside the decoder path.

Do not call `Apply Setup` or allocate large temporary arrays inside `TSMPBeforeEncode()`. It can run every encoded frame.

## Receiving lifecycle

The decoder sets the received field value, sets `lastVariableHash`, then calls `OnTSMPVariableReceived()`.

Override `OnTSMPVariableReceived()` when you need to:

- Apply a packed byte array.
- Update target interpolation values.
- Ignore values when `receiveInterpolation == None`.
- Apply only the variable that changed.

Call `base.OnTSMPVariableReceived()` if you want the base `lastVariableHash` handling.

## Packed byte arrays

For high-frequency data, pack your state into a `byte[]`.

Good candidates:

- Transforms.
- Bone rotations.
- Blend shape weights.
- Multiple small numeric fields.
- Per-player pose records.

Packed arrays keep payload size predictable and reduce Udon bridge calls. A single `byte[]` field is usually cheaper than many scalar fields.

## RPC events

Use `SendTransRPC` for event-like actions.

```csharp
public override void Interact()
{
    SendTransRPC(nameof(ToggleObject), RPCTarget.All);
}

public void ToggleObject()
{
    target.SetActive(!target.activeSelf);
}
```

`RPCTarget.All` runs the method locally immediately and queues it for receivers through the TSMP stream. In a loopback test, this means the event happens once now and once again after the transport delay.

Use RPC for events, not continuous state. Continuous state belongs in `[TransSync]` fields.

## Setup requirements

After adding a custom behaviour:

1. Add it to a scene or prefab.
2. Run `Apply Setup`.
3. Confirm it receives a network ID.
4. Confirm its `[TransSync]` fields appear in generated bindings.
5. Test with `TSMPDebugCanvas` visible.

If your component sends no data, check active state, `[TransSync]` direction, and whether `TSMPBeforeEncode()` is producing a non-empty field.

## UdonSharp compatibility checklist

- Avoid generic methods on UdonSharp behaviours.
- Avoid LINQ and reflection in runtime paths.
- Use explicit `for` loops.
- Prefer `byte[]` for packed data.
- Keep public synced fields simple.
- Test UdonSharp compile after changing field types.
