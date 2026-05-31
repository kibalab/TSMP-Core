---
title: Codec Validation
---

# Codec validation

Custom codec은 world에서 사용하기 전에 검증해야 합니다. Codec은 compile되더라도 camera capture, scaling, readback, package import 이후 실패할 수 있습니다.

## Basic checks

| Check | Expected result |
| --- | --- |
| Codec appears in Setup | Catalog와 prefab discovery가 동작. |
| Author and description appear | Package metadata를 읽을 수 있음. |
| Encode one frame | Output texture가 바뀌고 header를 읽을 수 있음. |
| Decode one frame | Header CRC가 통과하고 payload bytes가 복구됨. |
| Switch codec away and back | Stale pixel block이 남지 않음. |

## Byte round-trip test

Deterministic payload를 사용하세요.

- Increasing bytes `0, 1, 2, ...`.
- Alternating bytes `0x00, 0xFF`.
- Fixed seed random bytes.
- 한 row보다 짧은 payload.
- Row 또는 block boundary에서 정확히 끝나는 payload.

Decode 후 `PayloadSize`까지 모든 byte를 비교합니다.

## Header and layout test

Codec이 다음을 지키는지 확인하세요.

- Header size와 header location.
- `PayloadSize`.
- Payload start row 또는 block.
- Block size.
- Decode sample size.
- Codec option bytes.

Decoder가 visible pixels에서 payload size를 추측해야 하면 안 됩니다.

## Capture path test

사용자가 실제로 쓸 path로 test하세요.

- In-editor loopback.
- VRChat runtime capture.
- World가 사용하는 경우 OBS/Spout 또는 equivalent.
- Intended frame size.
- Intended frame rate.

`TSMPDebugCanvas`에서 bitrate, loss, frame index, codec ID, decoder errors를 확인하세요.

## UdonSharp test

Codec에 UdonSharp runtime code가 있다면:

- 모든 UdonSharp program을 compile합니다.
- Play mode에서 test합니다.
- Client runtime API에 의존하는 기능은 uploaded VRChat world에서 test합니다.
- Generic methods, unsupported APIs, UdonSharp binder failure를 유발하는 복잡한 expression을 피하세요.

## Failure patterns

| Symptom | Likely cause |
| --- | --- |
| Header valid but payload wrong | Payload start/block layout mismatch. |
| CRC warning | Header pixels corrupted 또는 sampling issue. |
| Stale blocks after codec switch | Output texture가 완전히 overwrite 또는 clear되지 않음. |
| Works in editor but not runtime | UdonSharp API 또는 VRChat runtime API difference. |
| Works at one size only | Shader가 fixed texture size를 가정함. |
