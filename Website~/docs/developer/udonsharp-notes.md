---
title: UdonSharp Notes
---

# UdonSharp notes

TSMP components are written for dual Unity C# and UdonSharp use. This affects API shape and implementation style.

## Compile symbols

Important symbols:

| Symbol | Meaning |
| --- | --- |
| `UDONSHARP` | UdonSharp is available. TSMP behaviours inherit from `UdonSharpBehaviour`. |
| `COMPILER_UDONSHARP` | UdonSharp is compiling the script into Udon assembly. |
| `UNITY_EDITOR` | Unity editor environment. |

Prefer keeping most code visible to the IDE. Use `COMPILER_UDONSHARP` only around code that UdonSharp cannot compile.

## Avoid in Udon-facing code

- Generic methods on UdonSharp behaviours.
- Unsupported Unity APIs.
- Interface casts that Udon cannot resolve.
- Complex LINQ.
- Reflection at runtime.
- Implicit numeric conversions that UdonSharp misbinds.
- Treating arrays as a different array type.
- Object arrays that later hold inconsistent runtime types.

## Prefer

- Explicit `for` loops.
- Simple `int`, `float`, `bool`, `byte`, `ushort`, `uint` values.
- Packed `byte[]` data for repeated values.
- Static helper classes for pure logic.
- Public fields for Udon bridge data.
- Small methods with simple control flow.
- Explicit casts when crossing numeric types.

## Bridge calls

Use `TSMPBehaviour.SetProgramVariable`, `GetProgramVariable`, and `SendCustomEvent` when writing code that must target both Udon and Mono behaviours.

The bridge handles editor proxy lookup where possible and falls back to Udon runtime calls when compiled for Udon.

Bridge calls are useful for framework code. Ordinary custom sync components should usually access their own fields directly.

## Runtime cost

Udon bridge calls are expensive. For high-frequency sync:

- Pack multiple values into one `byte[]`.
- Call `SetProgramVariable` once per packed field instead of once per scalar.
- Avoid per-bone or per-blendshape bridge calls when a packed packet can represent all values.
- Reuse buffers where possible.
- Keep receive apply code short.

## Debugging Udon-only failures

If a component works in Unity C# but fails in VRChat:

1. Compile all UdonSharp programs.
2. Check for stale serialized Udon fields.
3. Look for unsupported APIs in the failing method.
4. Check array types and numeric casts.
5. Add `Debug.LogWarning` diagnostics for runtime-visible state.

Inspector-only diagnostics are not enough for uploaded worlds.
