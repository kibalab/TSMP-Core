---
title: TSMPEncoder
---

# TSMPEncoder

`TSMPEncoder`는 송신자 쪽에서 사용합니다. TSMP 데이터를 출력 렌더 텍스처에 씁니다.

대부분의 사용자는 모든 필드를 직접 수정하기보다 `TSMPSetup`을 통해 설정합니다.

자동 변수는 [TransSync 송신 스케줄링](../scripting-api/transsync.md)을 따릅니다. 변경 없는 값은 기본적으로 1초마다 다시 전송하므로, 정지한 씬에서 재전송 사이에 프레임 번호가 멈추는 것은 정상입니다. 다른 컴포넌트의 재전송 간격은 그대로 두고 특정 컴포넌트만 자주 보내려면 해당 컴포넌트에서 [Send Mode: Always](./network-behaviour.md#send-mode)를 선택하세요. 최소 간격과 용량 제한은 계속 적용됩니다.

## 필요한 것

- 출력 `RenderTexture`.
- `TSMPSetup`을 통해 선택된 Luma4 handler.
- 하나 이상의 enabled TSMP network component 또는 queued RPC call.
- 선택한 데이터를 담을 수 있는 payload capacity.

## 사용 방법

1. `TSMPSetup`에서 output texture를 지정합니다.
2. Codec 탭에서 Luma4를 선택합니다.
3. Target frame rate를 설정합니다.
4. Setup 자동 준비가 끝났는지 확인합니다. `Apply Setup`은 선택적인 수동 갱신입니다.
5. 동기화 오브젝트를 움직이면서 output texture를 확인합니다.

Payload data가 바뀌면 output texture도 눈에 띄게 변해야 합니다. 계속 flat하거나 정적인 패턴만 보인다면 setup reference와 payload diagnostics를 확인하세요.

## 정상 출력의 모습

건강한 TSMP output에는 보통 다음이 있습니다.

- 구조적인 header 영역.
- 동기화 값이 바뀔 때 변화하는 payload 영역.
- 현재 payload보다 texture capacity가 클 때 남는 검은 영역 또는 unused space.

설정이나 payload size를 바꾼 뒤 이전 block 잔상이 남으면 output clearing이 켜져 있는지, 표시 중인 render texture가 실제 output인지 확인하세요.

## Payload와 capacity

Encoder는 프레임마다 하나의 payload를 만듭니다. Payload에는 다음이 들어갑니다.

- `[TransSync]` field에서 나온 variable state messages.
- `SendTransRPC`로 queue된 RPC messages.

자동 변수의 총량이 용량을 넘으면 우선순위가 높은 필드부터 기록하고 나머지는 이후로 미룹니다. **Deferred Variables**와 빈도 제한된 경고로 확인할 수 있습니다. 필드 하나가 전체 페이로드보다 크면 필드 크기를 줄이거나 출력 용량을 늘려야 전송할 수 있습니다. 잘못된 값이나 너무 큰 RPC·수동 메시지는 여전히 인코딩 실패를 일으킬 수 있습니다.

대역폭이 큰 데이터:

- Full VRChat avatar pose sync.
- 많은 본을 선택한 humanoid pose.
- 큰 blend shape selection.
- 많은 active players.
- 많은 독립 transform objects.

## Payload 무결성

실제 encoder는 항상 56바이트 header에 payload CRC32를 기록하고 프레임을 프로토콜 0.2로 표시합니다. 일반 Unity와 Udon 출력에서 모든 코덱의 정확한 직렬화 payload 길이를 보호하며, payload 용량을 줄이거나 NetworkFrame body에 바이트를 추가하지 않습니다. 현재 decoder는 값이나 RPC를 적용하기 전에 이를 검증합니다.

구 decoder는 같은 frame layout을 읽지만 payload CRC를 검사하지 않습니다. Legacy header 생성 API는 0.1 기본값을 유지하므로, 이를 사용하는 커스텀 송신기에 payload 보호가 자동으로 생기지는 않습니다. [Wire layout과 호환 규칙](../concepts/protocol.md#payload-integrity)을 참고하세요. Checksum은 변경된 프레임을 검출하며 복구나 목표 발행률을 보장하지 않습니다.

## RPC 사용

`TSMPNetworkBehaviour`에서 다음처럼 호출합니다.

```csharp
SendTransRPC(nameof(MyMethod), RPCTarget.All);
```

Encoder는 짧은 이벤트가 텍스처 전송에서 누락될 가능성을 줄이기 위해 queued RPC를 여러 프레임에 포함합니다.

Target:

| Target | Sender | Receiver |
| --- | --- | --- |
| `Local` | 즉시 실행. | 전송하지 않음. |
| `Remote` | 로컬 실행 없음. | 디코드 후 실행. |
| `All` | 즉시 실행. | 디코드 후 다시 실행. |

## Runtime diagnostics

중요한 필드와 경고:

| Diagnostic | 의미 |
| --- | --- |
| Frame index | 프레임이 인코딩될 때 증가합니다. |
| Payload bytes | 이번 프레임에 쓰인 payload bytes. |
| Message count | Payload 안의 network message 수. |
| `Payload buffer is full` | 현재 프레임에 모든 동기화 데이터를 담을 수 없습니다. |
| `Failed to write TSMP value` | 어떤 field를 encode하지 못했습니다. |
| Missing codec or output texture | Encoder가 유효한 frame을 만들 수 없습니다. |

## 흔한 해결 방법

| 증상 | 시도할 것 |
| --- | --- |
| Frame index가 증가하지 않음 | Auto encode를 켜거나 Debug 탭에서 Encode Now를 호출합니다. |
| Payload가 항상 0 | Enabled TSMP network component를 추가하고 `Apply Setup`을 실행합니다. |
| Payload buffer is full | 동기화 데이터를 줄이거나 output capacity를 늘립니다. |
| RPC가 누락됨 | Frame loss를 확인하고 RPC repeat frames를 유지합니다. |
| Codec 변경 후 output이 그대로 | `Apply Setup`을 다시 실행하고 output render texture가 clear되는지 확인합니다. |
