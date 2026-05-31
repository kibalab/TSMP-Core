---
title: Developer Testing
---

# Developer testing

Use this checklist when changing TSMP runtime code, adding a network behaviour, or adding a codec.

Testing should cover two things separately:

- Unity import and UdonSharp compile health.
- Runtime frame behaviour through a real texture path.

## Static checks

Run Unity C# builds where project files are available:

```powershell
dotnet build .\K13A.TSMP.Core.csproj --no-restore
dotnet build .\K13A.TSMP.Core.Editor.csproj --no-restore
```

These checks do not replace Unity import or UdonSharp compile, but they catch many C# reference issues early.

## Unity checks

- Unity imports without missing scripts.
- UdonSharp compile succeeds.
- `TSMPSetup` can refresh codecs.
- `Apply Setup` assigns bindings.
- Sample prefab references remain valid.
- Encoder and decoder inspectors show expected diagnostics.
- Luma4 appears in the Codec tab.

## Runtime smoke test

1. Place `TSMPController.prefab`.
2. Add `TSMPNetworkGameObjectToggle`.
3. Apply setup.
4. Confirm local interact toggles immediately.
5. Confirm decoded RPC toggles after loopback delay.
6. Add `TSMPNetworkTransformSync`.
7. Confirm payload bytes and message count are non-zero.
8. Confirm decoder applies values.

This test proves the minimal end-to-end path before high-bandwidth pose data is introduced.

## Protocol tests

- Valid frame header passes CRC validation.
- Mutated header byte fails CRC validation.
- Empty `VariableState` messages are not counted.
- RPC-only frames are encoded.
- Payload copy/FEC references are not reintroduced.
- Payload capacity boundaries reject oversized frames.

## Codec tests

- Header region decodes correctly.
- Payload capacity matches actual writable data.
- Payload start row matches decoder shader expectations.
- Codec option bytes survive the header.
- Unused output area is cleared or ignored.
- Decode works after texture transport through the target capture path.

## Regression checklist

Before releasing, test:

- Core plus Luma4 default install.
- Editor-time encoding.
- Unity Play mode encoding and decoding.
- Uploaded VRChat runtime.
- OBS/Spout loopback if the release changes transport, codec, or shader behaviour.
