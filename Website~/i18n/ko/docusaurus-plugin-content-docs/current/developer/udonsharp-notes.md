---
title: UdonSharp 메모
---

# UdonSharp notes

TSMP 컴포넌트는 Unity C#과 UdonSharp 양쪽에서 사용되도록 작성됩니다. 이 때문에 API 모양과 구현 스타일에 제약이 있습니다.

## Compile symbols

중요한 symbol:

| Symbol | 의미 |
| --- | --- |
| `UDONSHARP` | UdonSharp를 사용할 수 있습니다. TSMP behaviours가 `UdonSharpBehaviour`를 상속합니다. |
| `COMPILER_UDONSHARP` | UdonSharp가 script를 Udon assembly로 compile 중입니다. |
| `UNITY_EDITOR` | Unity editor environment입니다. |

가능하면 대부분의 코드는 IDE에 보이게 유지하세요. `COMPILER_UDONSHARP`는 UdonSharp가 compile할 수 없는 코드 주변에만 사용합니다.

## Udon-facing code에서 피할 것

- UdonSharp behaviour의 generic methods.
- 지원되지 않는 Unity APIs.
- Udon이 resolve할 수 없는 interface casts.
- 복잡한 LINQ.
- Runtime reflection.
- UdonSharp가 잘못 bind할 수 있는 implicit numeric conversions.
- Array를 다른 array type처럼 다루기.
- 서로 다른 runtime type이 들어가는 object arrays.

## 선호할 것

- 명시적인 `for` loops.
- 단순한 `int`, `float`, `bool`, `byte`, `ushort`, `uint` 값.
- 반복 값에는 packed `byte[]`.
- Pure logic은 static helper classes.
- Udon bridge data에는 public fields.
- 단순한 control flow의 작은 methods.
- Numeric type을 넘나들 때 explicit casts.

## Bridge calls

Unity C#과 Udon 양쪽을 대상으로 해야 하는 framework code에서는 `TSMPBehaviour.SetProgramVariable`, `GetProgramVariable`, `SendCustomEvent`를 사용합니다.

Bridge는 가능한 경우 editor proxy lookup을 처리하고, Udon compile 시에는 Udon runtime call로 fallback합니다.

Bridge call은 framework code에 유용합니다. 일반 custom sync component는 보통 자기 field에 직접 접근하는 편이 낫습니다.

## Runtime cost

Udon bridge call은 비쌉니다. 고빈도 sync에서는:

- 여러 값을 하나의 `byte[]`에 pack합니다.
- Scalar마다 호출하지 말고 packed field 하나에 `SetProgramVariable`을 한 번 호출합니다.
- Bone 또는 blendshape마다 bridge call하지 말고 하나의 packet으로 표현합니다.
- 가능하면 buffer를 재사용합니다.
- Receive apply code를 짧게 유지합니다.

## Udon-only failure 디버깅

Unity C#에서는 되지만 VRChat에서 실패한다면:

1. 모든 UdonSharp program을 compile합니다.
2. Stale serialized Udon fields를 확인합니다.
3. 실패하는 method의 unsupported API를 찾습니다.
4. Array type과 numeric cast를 확인합니다.
5. Runtime-visible state에는 `Debug.LogWarning` diagnostics를 추가합니다.

Inspector-only diagnostics만으로는 업로드된 월드를 디버깅하기 어렵습니다.
