---
title: TSMPDecoder API
---

# `TSMPDecoder`

## 헤더와 본문 직접 출력

`useCombinedByteOutput`의 기본값은 `true`입니다. 예측 경로에서 지원 코덱은 복원한 56바이트 헤더와 본문을 전용 RGBA8 readback 텍스처에 직접 출력합니다. 별도 합치기 패스와 본문 중간 출력이 줄어듭니다. 원본 스냅샷, 헤더 CRC 검사, 정확한 길이 비교와 같은 이미지에서의 폴백은 유지됩니다. 전송 데이터그램은 바뀌지 않으며 일정 수신율을 보장하는 기능은 아닙니다.

셰이더가 `_TSMPHeaderTex`와 `_TSMPHeaderPixels`를 모두 구현해야 사용됩니다. 지원하지 않는 기존 코덱은 별도 합치기 경로를 유지합니다. 비교용 스위치는 Diagnostics > Advanced Decode에 있습니다. `combinedByteOutputCount`는 제출 횟수이며 이후 거절되거나 재처리된 예측도 포함합니다. 실제 수신 프레임 수가 아닙니다. `ResetDecodeDiagnostics()`로 초기화합니다.

`TSMPDecoder`는 TSMP texture를 읽고 header를 검증한 뒤, 선택된 codec으로 payload bytes를 복구하고 variable/RPC message를 bound behaviour에 적용합니다.

Custom receiver wiring, diagnostics 확인, frame이 무시된 이유를 debug할 때 사용하세요.

## Component fields

| Field | 용도 |
| --- | --- |
| `sourceTexture` | Encoded TSMP frame이 들어있는 texture. |
| `payloadByteTexture` | Codec shader가 payload bytes를 복구할 때 사용하는 render texture. |
| `shaderChannel` | 기본값은 Off입니다. 활성화하면 승인된 데이터를 셰이더 채널 0~3 중 하나로 발행합니다. |
| `codecHandlers` | Decoder가 사용할 수 있는 codec components. Header의 `codecId`가 handler를 선택합니다. |
| `applyEveryFrame` | Component update loop에서 decode합니다. 다른 script가 `DecodeNow()`를 호출한다면 끄세요. |
| `skipDuplicateFrames` | 현재 스트림의 중복·역순 프레임을 걸러냅니다. Inspector 이름은 Filter Frames입니다. |
| `frameWindowSize` | Window Size. 기본 256프레임, 실제 최소값 1입니다. 0번 프레임의 재시작 판정에 사용하며 버퍼를 할당하지 않습니다. |
| `flipY` | Capture path가 image를 뒤집는 경우 texture sampling을 반전합니다. |
| `useHeaderPayloadLayout` | Header payload metadata로 readback size를 결정합니다. 일반적으로 켜둡니다. |
| `payloadBytesOverride` | Test path용 manual payload byte count. |
| `decodeSafetyMode` | Malformed 또는 partial frame에 대한 추가 guard. |
| `usePredictedReadback` | 기본 true. 이전에 검증한 설정으로 헤더와 payload를 한 번에 읽습니다. 수동 레이아웃과 안전 모드에서는 사용하지 않습니다. |
| `useCombinedByteOutput` | 기본 true. 지원 코덱 셰이더가 헤더/payload 통합 readback 텍스처에 직접 출력합니다. |
| `overlapReadbacks` | 기본 true. 독립적으로 캡처한 최대 두 프레임을 동시에 처리하며, false이면 한 프레임만 수락합니다. |

`TSMPSetup`이 보통 source texture, byte texture, codec handlers, binding arrays를 할당합니다.

## Header validation

Decoder는 다음 경우 payload 메시지를 적용하기 전에 frame을 버립니다.

- Magic bytes가 TSMP가 아님.
- Header size가 지원되지 않음.
- Major protocol version이 지원되지 않음.
- Header CRC 불일치.
- Payload size가 texture layout capacity를 초과.

CRC failure는 warning log로 표시됩니다. 손상된 capture frame이 잘못 적용되는 것을 막기 위한 동작입니다.

## `DecodeNow()`

```csharp
public void DecodeNow()
```

현재 이미지를 캡처하고 비동기 디코딩을 시작합니다. `applyEveryFrame`이 꺼져 있어도 호출할 수 있지만 컴포넌트는 활성화되어 있어야 합니다. 기본값인 `overlapReadbacks=true`에서는 첫 프레임을 기다리는 동안 두 번째 프레임을 시작할 수 있습니다. 두 슬롯이 모두 차면 호출을 건너뛰며 별도의 이미지 큐는 쌓지 않습니다. false이면 한 프레임만 허용합니다. 결과 적용 중의 재진입 호출은 캡처하지 않습니다.

최초 디코딩과 폴백 경로의 실행 순서:

1. 현재 입력 이미지를 디코더의 스냅샷에 복사합니다.
2. 스냅샷에서 헤더 픽셀을 읽습니다.
3. Magic, version, payload layout, CRC를 검증합니다.
4. `codecId`로 codec handler를 선택합니다.
5. 같은 스냅샷에서 payload bytes를 디코딩합니다.
6. Network messages를 파싱합니다.
7. 변수를 적용하고 RPC를 호출합니다.

## 예측 readback {#predicted-readback}

유효한 헤더를 학습한 뒤에는 CPU가 헤더를 기다리지 않고, 이전 설정으로 현재 이미지의 Luma4 헤더 패스와 payload 패스를 실행합니다. 복원된 바이트를 별도의 linear RGBA8 타깃으로 묶어 한 번만 readback합니다. 이 바이트 타깃은 입력의 형식을 보존하는 스냅샷과 별개입니다.

현재 헤더의 magic, version, size, CRC와 기존 프레임 순서 검사를 통과해야 합니다. CRC 이전 바이트 중 프레임 번호·타임스탬프·payload 길이를 제외한 값은 캐시와 같아야 합니다. payload 길이는 별도로 **정확히 같은지** 검사하고, 해석된 block/sample/start-block 설정도 비교합니다. 길이가 늘거나 줄거나, 코덱·옵션·스트림·레이아웃이 달라지면 추측한 payload를 버리고 **같은 스냅샷**에서 실제 payload를 다시 요청합니다. 원본은 다시 캡처하지 않습니다. CRC 실패 시에는 검증되지 않은 데이터로 재시도하지 않고 프레임을 폐기합니다.

정확한 길이만 허용하므로 사용자 정의 코덱이 더 많은 바이트를 요청받았을 때 같은 접두 데이터를 반환한다고 가정하지 않습니다. 코덱 API와 기존 셰이더는 바뀌지 않으며 각 패스의 `PrepareDecode`도 계속 호출합니다. 비활성화, 입력 크기·방향 변경, 수동 레이아웃·안전 모드 사용 뒤에는 헤더를 다시 학습합니다. 결합 리소스가 없으면 기존 순차 경로를 사용합니다. 머티리얼 참조는 Editor와 빌드 준비 단계에서 자동 지정하고, 일반 Unity 런타임 생성 시에는 패키지 리소스에서 불러옵니다.

각 슬롯의 내부 readback 버퍼에서 `0..55`는 헤더, `56`부터는 예상 payload이며 나머지는 행 패딩입니다. **송신 데이터그램 변경은 아닙니다.** 슬롯마다 14x1 헤더 텍스처와 너비 `min(256, ceil((56 + payloadBytes)/4))`에 필요한 행 수를 가진 결합 텍스처를 소유하며 픽셀당 4바이트입니다. 타깃은 재사용합니다. 비활성화 시 유휴 리소스는 즉시 해제하고, 대기 슬롯은 취소한 뒤 콜백이 도착하면 해제합니다. 컴포넌트 제거 시 소유 타깃을 해제합니다.

`predictedReadbackCount`는 채택한 예측 payload 수로, RPC 실행 성공 횟수가 아닙니다. `predictionFallbackCount`는 유효한 헤더에서 예측 불일치로 폴백한 횟수입니다. Runtime Status에서 확인할 수 있습니다. 중복·역순 프레임에서도 이미 추측한 GPU 작업이 실행됐을 수 있지만 메시지를 파싱하거나 적용하지 않습니다. 60Hz 전달을 보장하지 않으므로 적용률·지연·RPC 실행률을 따로 측정해야 합니다.

## 입력 스냅샷 {#input-snapshot}

Inspector에 추가 설정은 필요 없습니다. 슬롯마다 입력과 같은 크기의 RenderTexture를 소유하고 재사용합니다. 확인 가능한 RGBA/BGRA 8비트 입력은 linear/sRGB 해석을 유지한 `ARGB32`, RGBA half-float 입력은 linear `ARGBHalf`를 사용합니다. Float32와 알 수 없는 입력은 linear `ARGBFloat`를 유지합니다. Native Unity는 graphics format을 조회하고, Udon은 RenderTexture의 format과 sRGB 상태만 조회하므로 다른 입력 타입에는 보수적으로 Float32를 사용합니다. 형식이 바뀌면 유휴 저장소를 다시 생성합니다. 할당 실패는 `lastError`로 알리고 디코딩을 거부하며, 갱신 중인 원본으로 폴백하지 않습니다.

할당된 슬롯별 스냅샷 저장량은 `width * height * bytesPerPixel`이며 위 형식별로 픽셀당 4·8·16바이트입니다. 8비트 스냅샷 두 장은 1280x720에서 약 7.03 MiB, 3840x2160에서 약 63.28 MiB로 Float32 두 장보다 75% 작습니다. 다른 텍스처와 드라이버 비용은 제외한 수치입니다. 시작한 작업마다 전체 입력을 한 번 복사하며 이후 CRC·중복 검사에서 거부되어도 복사 비용은 듭니다. CPU readback은 추가하지 않습니다. 배포 장치에서 GPU 비용을 측정하세요.

캡처 이후 `sourceTexture`를 교체하거나 제거해도 현재 작업의 이미지는 바뀌지 않습니다. 변경은 다음 작업에 반영됩니다. 비활성화하면 점유 중인 슬롯을 모두 취소하고 재활성화 뒤 도착한 이전 콜백도 폐기합니다. 취소된 슬롯은 요청 완료 전까지 재사용하지 않습니다. native와 Udon 모두 헤더·LUT 준비·payload에 같은 스냅샷을 사용합니다. 사용자 정의 코덱은 이미지를 읽기 전용으로 다루고 영구적인 복사본처럼 보관하지 않아야 합니다. 적용은 트랜잭션 방식이 아닙니다.

## 제한된 readback 중첩 {#bounded-readback-overlap}

`overlapReadbacks`는 Diagnostics > Advanced Decode에 있습니다. 두 슬롯은 스냅샷, 예측 메타데이터, 읽기 버퍼와 폴백 상태를 따로 보관합니다. Udon은 요청 객체 식별로, 일반 Unity는 슬롯별 콜백으로 결과를 연결합니다. 완료 순서를 가정하지 않습니다. 결과는 캡처 순서로 처리합니다. 두 번째 결과가 먼저 도착해도 첫 슬롯의 폴백까지 기다린 뒤 적용하며 기존 프레임 순서 검사와 RPC 중복 억제를 유지합니다. 실패하거나 취소된 앞 슬롯은 본문을 적용하지 않고 제거하여 뒤 결과가 진행할 수 있게 합니다.

`readbackInFlight`는 앞 결과를 기다리는 완료 슬롯까지 포함해 남은 프레임이 있음을 뜻합니다. `pendingDecodeCount`는 0..2입니다. `skippedBusyDecodeCount`는 용량 초과로 거절된 캡처 시도 수이지 고유한 유실 영상 프레임 수가 아닙니다. `ResetDecodeDiagnostics()`로 초기화합니다.

위 계산식은 슬롯별 기준이며, 수치 예시는 이미 **스냅샷 두 장을 합한 값**입니다. 바이트 타깃·관리 버퍼의 메모리는 별도입니다. 처리 중 중첩 옵션을 끄면 여유가 생길 때까지 새 요청을 제한하지만 이미 받은 프레임을 버리거나 슬롯 메모리를 즉시 해제하지는 않습니다. 더 많은 캡처 기회를 확보하는 대신 메모리·지연·GPU/CPU 비용이 증가할 수 있습니다. readback 자체를 빠르게 하거나, 60Hz를 보장하거나, 입력 텍스처에 도달하지 않은 프레임을 복구하는 기능은 아닙니다.

## 윈도우와 프레임 순서 {#frame-window}

필터가 켜져 있으면 수신 번호 `N`과 현재 스트림에서 마지막으로 적용에 성공한 번호 `P`를 비교합니다. 실제 윈도우 크기는 `W = max(1, frameWindowSize)`이며, 변경은 다음 헤더 판정부터 반영합니다.

1. 적용 이력이 없거나 `StreamId`가 다르면 순서 비교 없이 후보를 허용합니다.
2. `N == P`이면 중복으로 건너뜁니다.
3. `D = (N - P) modulo 2^32`를 계산합니다. `0 < D < 2^31`이면 정상 번호 순환을 포함해 새로운 프레임으로 허용합니다.
4. 그 외에도 `N == 0`이고 `P >= W`이면 재시작으로 추정해 허용합니다.
5. 나머지는 역순 또는 순서가 불분명한 프레임으로 건너뜁니다. UInt32 범위의 정확히 절반만큼 차이나는 경우도 0번 재시작 예외가 아니면 거부합니다.

허용은 payload 처리를 진행한다는 뜻이지 무조건 적용한다는 뜻은 아닙니다. 네트워크 프레임 적용에 성공해야 기준 스트림과 번호가 갱신됩니다. 손상된 payload, 취소된 요청, 헤더/readback 전용 안전 모드는 기준을 갱신하지 않습니다. 중복·역순으로 건너뛴 프레임은 기존 중복 처리처럼 `lastFrameValid=true`이며, 각각 `skippedDuplicateFrameCount`와 `skippedOutOfOrderFrameCount`에 집계되고 `lastError`에 상태가 표시됩니다. 추측한 바이트 디코딩·readback이 이미 완료됐어도 payload 메시지를 파싱하거나 실행하지 않습니다.

`W=256`이면 `100 -> 101 -> 100`의 마지막 100은 무시하고, `255 -> 0`은 거부하며, `256 -> 0 -> 1`은 재시작과 후속 프레임으로 허용합니다. `4294967295 -> 0`은 윈도우 크기와 관계없이 정상 순환입니다. `W=512`이면 `256 -> 0`은 거부합니다. 윈도우는 프레임 수 기준이지 시간, 슬라이딩 재생 기록, 텍스처 저장 묶음이 아닙니다.

세션 ID가 없는 추정 규칙이므로 지연된 과거 0번을 재시작으로 오인할 수 있습니다. 재시작의 0번이 유실되거나 한 윈도우 이전에 재시작하면 이 예외로 감지하지 못합니다. 초기화를 허용한 뒤에는 높은 번호의 과거 프레임도 새 프레임처럼 보일 수 있습니다. 다른 스트림은 허용하고 적용 성공 시 기준을 교체하며, 이전 스트림의 기록은 보관하지 않습니다. RPC 이벤트 중복 판정은 변경하거나 초기화하지 않습니다. `skipDuplicateFrames`를 끄면 중복·역순 검사와 재시작 규칙을 모두 우회하므로 녹화 영상 탐색에는 사용할 수 있지만 과거 변수값도 다시 적용됩니다.

### 데이터그램 호환성

데이터그램과 프로토콜 버전은 **변경하지 않습니다**. 헤더는 56바이트이고 payload 형식도 같습니다. `20..23`은 `StreamId` UInt32 LE, `24..27`은 `FrameIndex` UInt32 LE, `28..31`은 `TimestampMs` UInt32 LE이며 타임스탬프는 이 판정에 사용하지 않습니다. 예약 영역 `44..49`는 계속 0입니다. CRC32는 `0..51`을 계산해 `52..55`에 기록합니다. 윈도우 크기와 세션 ID는 전송하지 않습니다. 나머지는 [프레임 레이아웃](../concepts/protocol.md)을 참조하세요. 기존 송신기는 변경할 필요가 없으며 수신기의 프레임 채택 정책만 바뀝니다.

## `ResetDecodeDiagnostics()`

```csharp
public void ResetDecodeDiagnostics()
```

오류 로그 출력 예산과 예측 readback·폴백·통합 출력·용량 초과 캡처 카운터를 초기화합니다. 대기 중인 슬롯을 취소하거나 기존 프레임·RPC 카운터와 순서 판정에 쓰는 마지막 적용 스트림·프레임을 초기화하지는 않습니다.

## Diagnostics

| Member | 의미 |
| --- | --- |
| `lastFrameValid` | Last frame이 모든 decode stage를 통과했는지. |
| `lastHeaderValid` | Payload decode 전에 header가 valid였는지. |
| `lastError` | 마지막 decoder error 또는 warning text. |
| `lastNetworkMessageCount` | Decoded network frame의 message 수. |
| `lastAppliedVariableCount` | Behaviour에 적용된 variable 수. |
| `lastRpcCallCount` | Dispatch된 RPC message 수. |
| `lastCodecId` | Last valid header에서 읽은 codec ID. |
| `lastFrameIndex` | Header frame index. |
| `lastPayloadBytes` | Decoder가 사용한 payload byte count. |

## Binding application

Decoder는 `TSMPSetup`이 만든 binding arrays를 사용합니다. 각 binding은 다음을 연결합니다.

- `networkId`
- `variableHash`
- value type
- target behaviour
- target field name
- sync direction

Variable message가 도착하면 matching binding에만 적용합니다. RPC message는 matching `TSMPNetworkBehaviour`로 dispatch합니다.

## Runtime rules

- Header가 valid라도 알 수 없는 codec ID면 frame을 무시합니다.
- Target behaviour의 `ReceiveInterpolation.None`은 received values를 무시합니다.
- RPC는 event 성격입니다. Transport가 frame을 drop한다면 encoder repeat frames를 늘리세요.
- Uploaded world에서는 inspector field를 볼 수 없기 때문에 중요한 runtime failure는 log로 표시됩니다.

## 셰이더에서 수신 데이터 읽기

설정 절차와 ShaderLab 예제는 [머티리얼에서 TSMP 데이터 읽기](../guides/shader-receive.md)에 있습니다. [TSMP.cginc 레퍼런스](./shader-receive.md)에서 HLSL 함수, 바이트 뷰 읽기, 필드가 없을 때의 동작과 캐시 한도를 확인할 수 있습니다.
