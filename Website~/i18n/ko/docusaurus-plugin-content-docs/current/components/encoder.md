---
title: TSMPEncoder
---

# TSMPEncoder

`TSMPEncoder`는 송신자 쪽에서 사용합니다. TSMP 데이터를 출력 렌더 텍스처에 씁니다.

대부분의 사용자는 모든 필드를 직접 수정하기보다 `TSMPSetup`을 통해 설정합니다.

## 필요한 것

- 출력 `RenderTexture`.
- `TSMPSetup`을 통해 선택된 Luma4 handler.
- 하나 이상의 enabled TSMP network component 또는 queued RPC call.
- 선택한 데이터를 담을 수 있는 payload capacity.

## 사용 방법

1. `TSMPSetup`에서 output texture를 지정합니다.
2. Codec 탭에서 Luma4를 선택합니다.
3. Target frame rate를 설정합니다.
4. `Apply Setup`을 클릭합니다.
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

Payload가 너무 크면 encoder가 `Payload buffer is full`을 기록하고 모든 데이터를 포함할 수 없습니다. 먼저 동기화 데이터를 줄이고, 필요할 때 texture capacity를 늘리세요.

대역폭이 큰 데이터:

- Full VRChat avatar pose sync.
- 많은 본을 선택한 humanoid pose.
- 큰 blend shape selection.
- 많은 active players.
- 많은 독립 transform objects.

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
