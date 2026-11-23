---
title: TSMP.cginc
---

셰이더 타깃 3.5 이상의 **소비자 셰이더**에서 사용하는 include입니다. 디코더가 이미 검증해 발행한 값을 읽습니다. 인코딩된 영상 텍스처를 직접 디코딩하거나 코덱을 구현하는 용도는 아닙니다. 동작 예제는 [머티리얼에서 TSMP 데이터 읽기](../guides/shader-receive.md)를 참고하세요.

```hlsl
#pragma target 3.5
#include "Packages/com.kibalab.tsmp.core/Shaders/TSMP.cginc"
```

## 조회 및 읽기 함수

숫자 인자는 모두 32비트 부호 없는 정수입니다. 조회 함수는 `TSMPByteView`를 채우며, 요청한 데이터가 없으면 `false`를 반환합니다. `TSMPTryGetFrame(out view)`와 `TSMPTryGetField(networkId, hash, out view)`는 채널 0을 사용하는 축약형입니다.

| HLSL | 결과 |
| --- | --- |
| `bool TSMPTryGetFrame(channel, out view)` | 56바이트 전송 헤더를 제외한 최신 [NetworkFrame 페이로드](./network-frame.md). RPC 엔트리도 바이트로 읽을 수 있지만 이벤트 큐는 아닙니다. |
| `bool TSMPTryGetField(channel, networkId, hash, out view)` | 엔트리 헤더를 제외한 해당 키의 최신 값. 길이가 0인 값을 수신한 경우에도 true입니다. |
| `uint TSMPStreamId(channel)` | 발행된 프레임의 스트림 ID. 먼저 읽을 프레임이 있는지 확인하세요. |
| `uint TSMPFrameIndex(channel)` | 프레임 번호. 정상 프레임도 번호가 0일 수 있습니다. |
| `uint TSMPReadByte(view, offset)` | 1바이트 정수. |
| `uint TSMPReadUInt16LE(view, offset)` | 리틀 엔디언 2바이트 정수. |
| `uint TSMPReadUInt32LE(view, offset)` | 리틀 엔디언 4바이트 정수. |
| `float TSMPReadFloat32LE(view, offset)` | 4바이트 비트를 IEEE 754 Float32로 해석한 값. 정수를 실수로 변환하는 함수가 아닙니다. |
| `uint TSMPReadBytesLE(view, offset, count)` | 1~4바이트를 리틀 엔디언으로 읽습니다. 이외의 개수면 0입니다. |

뷰의 `length`는 바이트 단위입니다. `valueType`은 필드의 전송 타입 `NetworkValueType`이며, 전체 프레임 뷰에서는 0입니다. 오프셋은 **뷰 시작 기준**입니다. 읽기 함수는 비정렬 주소도 처리하며, 요청한 바이트 하나라도 범위를 벗어나면 0을 반환합니다. 값이 실제 0인지 데이터가 없는 것인지 구분하려면 조회 결과와 길이를 먼저 확인하세요. `view.channel`과 `view.offset`은 내부 주소 정보입니다. 같은 셰이더 호출 안에서는 뷰를 재사용하되 다음 프레임에서는 다시 조회하세요.

## 필드 선택 {#field-key}

필드 키는 송신자의 Network ID와 32비트 전송 해시로 구성됩니다. 바인딩이 있으면 **Copy Shader Lookup**으로 HLSL 식을 복사할 수 있습니다. 수신 바인딩이 없다면 C#에서 `StableHash.VariableHash(behaviour.GetType(), fieldName, transSyncKey)`로 계산하세요. 전체 동작 타입 이름, 점, TransSync 키(키가 비어 있으면 필드명)를 연결한 문자열의 해시입니다. `Shader.PropertyToID`와는 다르며 float로 저장하면 하위 비트가 유실될 수 있습니다. `0xFEDCBA98u`와 같은 `uint` 값을 사용하세요.

## 값 유지와 비용

SendOnChange로 생략된 필드는 마지막 값을 유지합니다. 한 프레임에 같은 키가 반복되면 마지막 엔트리가 남습니다. 스트림이 바뀌거나 프레임 번호 재시작을 승인하면 이전 필드는 지웁니다. 필터링·거부된 프레임이나 진단 안전 모드의 프레임은 발행하지 않습니다. 디코더를 끄거나 채널을 바꾸면 이전 데이터는 해제됩니다.

기본값 **Off**에서는 발행하지 않습니다. 켜면 발행 프레임마다 필드 위치표를 업로드하고 디코딩된 바이트를 읽는 GPU 패스를 한 번 수행합니다. GPU readback을 추가하거나 CPU 검증·바인딩·RPC를 대체하지 않습니다. 채널당 최대 1,024개 필드, 값 합계 1 MiB(필드마다 4바이트 단위로 올림)까지 유지합니다. 어느 한도를 초과하면 채널을 무효화하고 경고를 남깁니다. CPU 디코딩은 계속되고, 다음 발행 가능한 프레임부터 캐시를 다시 채웁니다.
