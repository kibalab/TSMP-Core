---
title: TSMPBehaviour
---

# TSMPBehaviour

Namespace: `K13A.TSMP`

`TSMPBehaviour` is the base type for TSMP runtime components.

Depending on compile symbols, it inherits from:

- `UdonSharpBehaviour` when `UDONSHARP` is defined.
- `MonoBehaviour` otherwise.

This allows TSMP components to share one source shape while still compiling for UdonSharp runtime and Unity editor/native runtime.

## Static bridge methods

| Method | Use |
| --- | --- |
| `SetProgramVariable(...)` | Set a field on a Udon or Mono target. |
| `GetProgramVariable(...)` | Read a field from a Udon or Mono target. |
| `SendCustomEvent(...)` | Invoke an event/method on a Udon or Mono target. |

Use these methods when framework code must work with both Unity C# components and Udon behaviours.

In ordinary custom components, prefer direct field access on `this`. Bridge calls are more useful for encoder, decoder, binding, and dispatch code.

## Logging helpers

`TSMPBehaviour` provides protected rate-limited logging helpers used by runtime components:

| Method | Purpose |
| --- | --- |
| `ResetTSMPLogBudget(int budget)` | Resets the number of allowed debug/error messages. |
| `LogTSMPError(string prefix, string error, bool enabled)` | Emits an error while budget remains. |
| `LogTSMPWarning(string prefix, string error, bool enabled, float intervalSeconds)` | Emits a warning with budget and interval control. |
| `LogTSMPRateLimitedWarning(string prefix, string error, float intervalSeconds)` | Emits a warning with interval control only. |

Use these when adding TSMP components that need runtime-visible diagnostics without flooding VRChat logs.

## When to derive directly

Derive directly from `TSMPBehaviour` for TSMP runtime components that are not synchronized by network ID, such as utility components, debug UI, or avatar rig helpers.

Derive from `TSMPNetworkBehaviour` when the component sends or receives TSMP network messages.
