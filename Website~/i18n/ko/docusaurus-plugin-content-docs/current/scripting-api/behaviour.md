---
title: TSMPBehaviour
---

# TSMPBehaviour

Namespace: `K13A.TSMP`

`TSMPBehaviour`는 TSMP runtime component의 base type입니다.

Compile symbol에 따라 다음을 상속합니다.

- `UDONSHARP`가 정의되면 `UdonSharpBehaviour`.
- 그렇지 않으면 `MonoBehaviour`.

이를 통해 TSMP component가 UdonSharp runtime과 Unity editor/native runtime에서 같은 source shape를 공유할 수 있습니다.

## Static bridge methods

| Method | 용도 |
| --- | --- |
| `SetProgramVariable(...)` | Udon 또는 Mono target의 field를 설정합니다. |
| `GetProgramVariable(...)` | Udon 또는 Mono target에서 field를 읽습니다. |
| `SendCustomEvent(...)` | Udon 또는 Mono target의 event/method를 호출합니다. |

Framework code가 Unity C# component와 Udon behaviour 양쪽에서 동작해야 할 때 사용합니다.

일반 custom component에서는 `this`의 field에 직접 접근하는 것을 선호하세요. Bridge call은 encoder, decoder, binding, dispatch code에 더 적합합니다.

## Logging helpers

`TSMPBehaviour`는 runtime component가 사용하는 protected rate-limited logging helper를 제공합니다.

| Method | 목적 |
| --- | --- |
| `ResetTSMPLogBudget(int budget)` | 허용할 debug/error message 수를 reset합니다. |
| `LogTSMPError(string prefix, string error, bool enabled)` | Budget이 남아 있을 때 error를 출력합니다. |
| `LogTSMPWarning(string prefix, string error, bool enabled, float intervalSeconds)` | Budget과 interval을 적용해 warning을 출력합니다. |
| `LogTSMPRateLimitedWarning(string prefix, string error, float intervalSeconds)` | Interval만 적용해 warning을 출력합니다. |

VRChat log를 flood하지 않으면서 runtime-visible diagnostics가 필요한 TSMP component에 사용하세요.

## 직접 상속할 때

Network ID로 동기화되지 않는 TSMP runtime component라면 `TSMPBehaviour`를 직접 상속합니다. 예: utility component, debug UI, avatar rig helper.

TSMP network message를 보내거나 받는 component는 `TSMPNetworkBehaviour`를 상속하세요.
