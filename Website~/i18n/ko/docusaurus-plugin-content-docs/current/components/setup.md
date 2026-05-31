---
title: TSMPSetup
---

# TSMPSetup

`TSMPSetup`은 씬을 TSMP용으로 준비하는 컴포넌트입니다.

TSMP 씬이 바뀔 때마다 가장 먼저 확인해야 하는 컴포넌트입니다. 인코더, 디코더, 코덱, 렌더 텍스처, 네트워크 ID, 생성된 바인딩 데이터를 연결합니다.

## 언제 필요한가

씬 구조가 바뀔 때마다 setup을 실행하세요. 이렇게 해야 encoder, decoder, Luma4 참조, network ID, binding table이 서로 맞게 유지됩니다.

다음 작업 후에는 `Apply Setup`을 클릭하세요.

- `TSMPNetwork*` 컴포넌트를 추가하거나 제거한 경우.
- 어떤 오브젝트를 인코딩/디코딩할지 바꾼 경우.
- Encoder output render texture를 교체한 경우.
- Decoder source texture를 교체한 경우.
- Codec selection을 바꾼 경우.
- Manual network ID를 바꾼 경우.
- TSMP 컴포넌트를 prefab 안팎으로 옮긴 경우.

인코딩 텍스처는 변하지만 수신 오브젝트가 움직이지 않는다면 다른 디버깅보다 먼저 `Apply Setup`을 실행하세요.

## 일반 작업 흐름

1. `TSMPController.prefab`을 씬에 넣습니다.
2. Reference 탭에서 텍스처와 씬 참조를 지정합니다.
3. Codec 탭에서 Luma4가 선택되어 있는지 확인합니다.
4. 오브젝트에 TSMP sync 컴포넌트를 추가합니다.
5. `Apply Setup`을 클릭합니다.
6. Play 모드 또는 VRChat에서 테스트합니다.
7. 오브젝트를 움직이거나 상호작용하는 동안 `TSMPDebugCanvas`를 봅니다.

## 탭

| 탭 | 용도 |
| --- | --- |
| Reference | Encoder, decoder, textures, render textures, shared scene references 지정. |
| Codec | Luma4 handler 선택, codec package metadata 확인, codec discovery refresh. |
| Bindings | 동기화 behaviour, generated bindings, network IDs 확인. |
| Debug | 수동 encode 실행과 setup diagnostics 확인. |

## 먼저 지정할 것

일반 프리팹 설정에서는 `Apply Setup`을 누르기 전에 다음을 확인하세요.

| 필드 그룹 | 지정할 것 |
| --- | --- |
| Encoder | 컨트롤러 프리팹의 `TSMPEncoder`. |
| Decoder | 컨트롤러 프리팹의 `TSMPDecoder` 또는 수신자 decoder. |
| Encoder output | 캡처되거나 표시될 render texture. |
| Decoder source | 수신된 TSMP 이미지를 담는 texture. |
| Payload byte texture | payload byte를 읽는 동안 사용하는 decoder work texture. |
| Codec | Luma4 handler. |

디코더가 OBS나 다른 캡처 경로에서 받는 경우 `Decoder source`는 원본 encoder output이 아니라 캡처된 texture여야 합니다.

## Apply Setup

`Apply Setup`은 단순히 설정을 저장하는 버튼이 아닙니다. Encoder와 decoder가 사용하는 runtime data를 갱신합니다.

- 선택한 codec reference 지정.
- Frame layout과 payload capacity 계산.
- 설정된 경우 TSMP render texture resize.
- Encoder와 decoder binding table 생성.
- Network ID 할당 또는 갱신.
- Codec decode material 설정.
- 필요한 runtime codec instance 생성.

적용 후 Bindings 탭을 확인하세요. 동기화되길 기대한 컴포넌트가 목록에 없다면 encoder가 그 데이터를 보내지 않습니다.

## 안전한 기본값

처음에는 다음을 권장합니다.

- Automatic codec discovery enabled.
- Luma4 selected.
- Automatic setup enabled.
- Generated network IDs.
- Default block size and sample size.
- 테스트 중 `TSMPDebugCanvas` 표시.

기본 스트림이 동작한 뒤에만 manual ID나 custom reference를 고정하세요.

## 흔한 설정 실수

| 증상 | 가능성 높은 설정 문제 |
| --- | --- |
| Encoder는 실행되지만 `payload=0` | Enabled TSMP network component가 없거나 setup을 다시 적용하지 않았습니다. |
| Decoder는 frame을 보지만 아무것도 적용하지 않음 | Binding이 receiver component와 맞지 않거나 receive interpolation이 `None`입니다. |
| Luma4가 없음 | Codec package 또는 catalog가 import/discover되지 않았습니다. |
| 플레이어를 추가하면 데이터가 사라짐 | 새 데이터셋에 비해 payload capacity가 작습니다. |
| Editor output이 이전 codec처럼 보임 | Codec selection 변경 후 setup/material을 다시 적용하지 않았습니다. |
