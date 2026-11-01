---
title: Developer Testing
---

# Developer testing

Use this checklist when changing TSMP runtime code, adding a network behaviour, or adding a codec.

Testing should cover two things separately:

- Unity import and UdonSharp compile health.
- Runtime frame behaviour through a real texture path.

## Reproducible automated checks

From the repository root, use `Validation~/Run-Suite.ps1` with separate dedicated Native and Worlds SDK projects containing the exact Core and Luma4 sources under test:

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
& './Validation~/Run-Suite.ps1' -UnityEditor $unity -Project '<Native project>' -Results '<evidence root>' -Profile Native
& './Validation~/Run-Suite.ps1' -UnityEditor $unity -Project '<SDK project>' -Results '<evidence root>' -Profile Sdk
```

The suite prepares the project, retains original logs and records failed dependencies as `NOTRUN`. After parsing or scheduling changes, add an explicit run with `-Steps FrameStructure,PayloadBudget` in each profile. The same entry point delegates to the existing targeted runners and selects compiled client VM coverage for SDK projects. These steps are opt-in and do not replace the default real-texture loopback.

`Validation~/README.md` is the current results index and command reference. Its WorkloadFollowup, BindingCost, RpcBurst, Color256Followup and NextImprovements links distinguish tested revisions, failed controls and scope. Long measurements and source A/B controllers remain separate, explicit commands. Record useful received update rate, source age and RPC application delay; packet acceptance alone does not establish useful delivery. Do not add reductions from separate experiments or convert Editor VM timings into live FPS.

Automated Editor/Player/VM results do not certify a fresh VPM download, Quest, a populated VRChat world or long-running OBS–VRCDN transport. Test these separately when they are in release scope. Development-branch evidence also does not imply that the same changes are in the published package.

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
- Mutated payload fails CRC validation before applying state or RPCs.
- A valid-CRC frame with a malformed later message is rejected before any state/RPC application; retrying the corrected event still works.
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
