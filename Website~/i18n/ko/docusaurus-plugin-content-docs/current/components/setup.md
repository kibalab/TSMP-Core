---
title: TSMPSetup
---

# TSMPSetup

`TSMPSetup`은 씬을 TSMP용으로 준비하는 컴포넌트입니다.

인코더, 디코더, 코덱, 렌더 텍스처, 네트워크 ID, 생성된 바인딩 데이터를 연결합니다. VRChat SDK의 유무와 관계없이 같은 컨트롤러 프리팹과 인스펙터를 사용합니다.

## 언제 필요한가

씬의 컨트롤러는 임포트 후, Hierarchy나 Setup 설정이 바뀔 때 자동으로 준비됩니다. 씬 저장, Play 모드 진입, 빌드 전에도 준비합니다. 별도의 변환 메뉴나 SDK 없는 환경만을 위한 추가 설정 단계는 필요하지 않습니다.

다음과 같은 변경 사항이 자동으로 반영됩니다.

- `TSMPNetwork*` 컴포넌트를 추가하거나 제거한 경우.
- 어떤 오브젝트를 인코딩/디코딩할지 바꾼 경우.
- Encoder output render texture를 교체한 경우.
- Decoder source texture를 교체한 경우.
- Codec selection을 바꾼 경우.
- Manual network ID를 바꾼 경우.
- TSMP 컴포넌트를 prefab 안팎으로 옮긴 경우.

인코딩 텍스처는 변하지만 수신 오브젝트가 움직이지 않는다면 Bindings 탭과 디코더의 입력 텍스처를 확인하세요. `Apply Setup`은 수동 갱신이 필요할 때 사용할 수 있으며, 매번 눌러야 하는 버튼은 아닙니다.

## 일반 작업 흐름

1. `TSMPController.prefab`을 씬에 넣습니다.
2. Reference 탭에서 텍스처와 씬 참조를 지정합니다.
3. Codec 탭에서 Luma4가 선택되어 있는지 확인합니다.
4. 오브젝트에 TSMP sync 컴포넌트를 추가합니다.
5. Bindings 탭에 동기화할 오브젝트가 표시되는지 확인합니다.
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

프리팹은 인코더, 디코더, 코덱, 작업 텍스처 참조를 자동으로 준비합니다. 씬에 맞게 다음 항목을 확인하거나 변경하세요.

| 필드 그룹 | 지정할 것 |
| --- | --- |
| Encoder | 컨트롤러 프리팹의 `TSMPEncoder`. |
| Decoder | 컨트롤러 프리팹의 `TSMPDecoder` 또는 수신자 decoder. |
| Encoder output | 캡처되거나 표시될 render texture. |
| Decoder source | 수신된 TSMP 이미지를 담는 texture. |
| Payload byte texture | payload byte를 읽는 동안 사용하는 decoder work texture. |
| Codec | Luma4 handler. |

디코더가 OBS나 다른 캡처 경로에서 받는 경우 `Decoder source`는 원본 encoder output이 아니라 캡처된 texture여야 합니다.

기본 컨트롤러는 로컬 테스트를 위해 인코더 출력을 디코더 입력으로 사용합니다. VRChat SDK를 설치해도 이 설정은 바뀌지 않습니다. 외부 스트림을 수신할 때는 수신 이미지를 명시적으로 지정하세요.

패키지의 렌더 텍스처와 머티리얼은 템플릿입니다. 각 컨트롤러가 사용할 복사본은 `Assets/TSMPGenerated`에 자동 생성되므로 씬과 함께 버전 관리에 포함하세요. 컨트롤러를 복제하면 작업 텍스처도 분리됩니다. 사용자가 패키지 밖에서 직접 지정한 텍스처는 유지됩니다.

## Apply Setup

자동 준비와 선택적인 `Apply Setup` 버튼은 동일한 런타임 데이터를 갱신합니다.

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
- 참조를 준비할 수 있도록 컨트롤러를 씬에 유지.
- Generated network IDs.
- Default block size and sample size.
- 테스트 중 `TSMPDebugCanvas` 표시.

기본 스트림이 동작한 뒤에만 manual ID나 custom reference를 고정하세요.

## 흔한 설정 실수

| 증상 | 가능성 높은 설정 문제 |
| --- | --- |
| Encoder는 실행되지만 `payload=0` | 설정한 바인딩 범위 안에 활성화된 TSMP 네트워크 컴포넌트가 없습니다. |
| Decoder는 frame을 보지만 아무것도 적용하지 않음 | Binding이 receiver component와 맞지 않거나 receive interpolation이 `None`입니다. |
| Luma4가 없음 | Codec package 또는 catalog가 import/discover되지 않았습니다. |
| 플레이어를 추가하면 데이터가 사라짐 | 새 데이터셋에 비해 payload capacity가 작습니다. |
| Editor output이 이전 codec처럼 보임 | 선택한 코덱, 인코더 참조, 현재 보고 있는 출력 텍스처가 해당 컨트롤러의 것인지 확인하세요. |
