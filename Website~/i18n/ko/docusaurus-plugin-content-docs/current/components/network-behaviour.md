---
title: TSMPNetworkBehaviour
---

# TSMPNetworkBehaviour

`TSMPNetworkBehaviour`는 TSMP가 보내거나 받을 수 있는 데이터의 기본 컴포넌트입니다.

대부분의 사용자는 [Sync components](./sync-components.md)에 있는 기본 TSMP 네트워크 컴포넌트를 사용합니다. 각 컴포넌트에 공통으로 보이는 설정만 이해하면 됩니다.

## Network ID

각 동기화 컴포넌트에는 network ID가 있습니다. Sender와 receiver는 이 ID로 message를 object에 매칭합니다.

ID 할당과 갱신은 `TSMPSetup`을 사용하세요. 고정 mapping이 꼭 필요할 때만 수동으로 수정합니다.

규칙:

- 같은 stream을 공유하는 active TSMP network behaviour 사이에서 ID는 고유해야 합니다.
- Sender와 receiver가 같은 ID를 사용해야 합니다.
- Component를 추가, 삭제, 이동한 뒤에는 `Apply Setup`을 다시 실행해야 합니다.
- 두 behaviour가 같은 network ID를 공유하면 receiver가 잘못된 object에 데이터를 적용하거나 message를 무시할 수 있습니다.

Manual ID는 고급 옵션으로 취급하세요. Inspector는 실수로 바꾸는 것을 막기 위해 기본적으로 필드를 잠급니다.

## Receive interpolation

컴포넌트별 receive interpolation을 설정합니다.

| Mode | 사용 시점 |
| --- | --- |
| None | 이 object가 수신 값을 적용하면 안 되는 경우. sender-only object에 유용합니다. |
| Discrete | 최신 수신 상태로 즉시 snap해야 하는 경우. event, toggle, animator state, 낮은 빈도의 값에 적합합니다. |
| Continuous | 수신 상태 사이를 부드럽게 이동해야 하는 경우. transform과 pose target에 적합합니다. |

`None`은 송신을 막지 않습니다. 해당 컴포넌트에서 들어오는 값을 무시한다는 뜻입니다.

## Active state

TSMP는 component와 GameObject active state를 기본 on/off switch로 사용합니다.

- Component를 비활성화하면 해당 behaviour가 참여하지 않습니다.
- GameObject를 비활성화하면 그 위의 모든 TSMP behaviour가 멈춥니다.
- 동기화 object를 추가, 제거, 구조적으로 이동한 경우 setup을 다시 실행하세요.

일시적인 런타임 enable/disable은 encode와 receive 중에 반영됩니다. 영구적인 씬 구조 변경은 `Apply Setup`을 다시 실행해야 합니다.

## RPC 보내기

`TSMPNetworkBehaviour`에서 `SendTransRPC`를 사용합니다.

```csharp
SendTransRPC(nameof(ToggleObject), RPCTarget.All);
```

Target:

| Target | 동작 |
| --- | --- |
| `Local` | 호출자만 method를 실행합니다. TSMP로 보내지 않습니다. |
| `Remote` | 수신자만 stream delay 뒤 method를 실행합니다. |
| `All` | 호출자는 즉시 실행하고, 수신자는 stream delay 뒤 실행합니다. |

최소 예제는 `TSMPNetworkGameObjectToggle`을 사용하세요.

## Receiver object가 업데이트되지 않을 때

확인할 것:

1. Receiver component가 enabled이고 active인지.
2. Receive interpolation이 `None`이 아닌지.
3. Network ID가 sender와 일치하는지.
4. Component 추가 후 `TSMPSetup`을 적용했는지.
5. Decoder message count가 0보다 큰지.
