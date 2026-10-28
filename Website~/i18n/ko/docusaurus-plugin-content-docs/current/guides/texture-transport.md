---
title: 텍스처 전송
---

# 텍스처 전송

TSMP는 인코딩된 픽셀이 encoder에서 decoder까지 살아남아야 동작합니다.

TSMP 영역은 데이터 버스처럼 다루세요. 시각적으로는 사소해 보이는 변화도 바이트를 손상시킬 수 있습니다.

## 기본 규칙

TSMP 영역은 영상이 아니라 데이터처럼 복사되어야 합니다. 가능한 한 정확히 유지하세요.

피해야 할 것:

- TSMP 이미지 scaling.
- Bilinear 또는 trilinear filtering.
- Anti-aliasing.
- Color grading.
- Tone mapping.
- TSMP 영역 위의 bloom 또는 post processing.
- 픽셀 값을 바꾸는 video compression.
- Header 또는 payload region cropping.

## VRChat screen-space output

VRChat camera capture에서는 TSMP output이 TSMP camera 또는 screen-space material로 표시되어야 합니다.

일반적인 설정:

1. TSMP output material을 cube 같은 mesh에 적용합니다.
2. VRChat camera를 그 mesh 안에 넣습니다.
3. TSMP 이미지가 capture view를 가득 채우게 합니다.
4. 전체 header와 payload region이 보이게 합니다.

Camera가 mesh 일부만 보면 decoder가 missing header, CRC mismatch, zero messages를 보고할 수 있습니다.

## 해상도

해상도가 높을수록 payload capacity는 커지지만 rendering과 readback 비용도 증가합니다.

샘플 프리팹 기본값으로 시작하세요. 다음 경우에만 해상도를 올리세요.

- `Payload buffer is full`이 나타납니다.
- Debug canvas에서 payload usage가 capacity에 가까워집니다.
- 이미 불필요한 sync data를 줄였습니다.

Transport 문제를 숨기려고 해상도를 올리지 마세요. CRC error는 픽셀을 보존해서 해결해야 합니다.

## Filtering settings

TSMP 데이터를 운반하는 텍스처:

- 가능한 곳에서 point filtering을 사용합니다.
- Mipmap을 피합니다.
- TSMP render texture의 anti-aliasing을 끕니다.
- Sender와 receiver의 color space와 sampling을 일관되게 유지합니다.
- TSMP 영역이 pixel-exact로 유지되는 것을 확신하지 않는다면 OBS에서 resize하지 않습니다.

## OBS와 capture settings

OBS를 사용할 때:

- OBS에서 Spout input/output이 필요하다면 Spout2 plugin을 설치합니다.
- 가능하면 TSMP output resolution 그대로 capture합니다.
- Scaling filter를 피합니다.
- Color correction filter를 피합니다.
- TSMP 영역을 완전히 보이게 합니다.
- Receiver input texture가 원본 encoder render texture가 아니라 OBS output인지 확인합니다.

## Unity에서 FFmpeg로 송출

`TSMPFfmpegRtmpPublisher`는 별도로 설치한 FFmpeg 실행 파일을 사용해 Unity 텍스처를 RTMP 서버로 송출합니다. 데스크톱 Unity용 컴포넌트이며, VRChat 내부에서 실행하는 Udon 컴포넌트가 아닙니다.

`Source Texture`, `FFmpeg Path`, RTMP 수신 주소를 지정한 뒤 `StartPublishing()`을 호출하거나 `Auto Start`를 켭니다. 에디터에서도 캡처하려면 `Publish In Edit Mode`를 켭니다. `StopPublishing()` 호출, 컴포넌트 비활성화 또는 삭제 시 송출이 종료됩니다.

`Use Source Dimensions`를 켜면 텍스처의 실제 해상도를 사용합니다. 끈 경우 `Width`와 `Height`가 원본과 정확히 같아야 합니다. 불일치하면 경고와 함께 시작을 거부하며 TSMP 픽셀을 임의로 리사이즈하지 않습니다. YUV420p는 너비와 높이가 모두 짝수여야 합니다.

해상도와 프레임레이트는 송출 시작 시 고정됩니다. 해당 설정이나 원본 해상도를 바꾼 뒤에는 송출을 종료하고 다시 시작하세요. 원본을 제거하거나 크기를 바꾸면 호환되지 않는 바이트를 보내기 전에 송출을 중단합니다. 재시작 이후 도착한 이전 세션의 GPU 요청 결과는 버립니다.

`Flip Vertical`은 행 크기를 유지하면서 위아래 순서만 뒤집습니다. `Repeat Last Frame When Idle`은 새 캡처가 없을 때 마지막 프레임을 반복합니다. 프로세스 오류는 `Last Error`와 경고 로그에 표시되며, 추가 출력은 `Log Ffmpeg Output`으로 확인할 수 있습니다. H.264 압축과 색상 변환은 여전히 데이터를 손상시킬 수 있으므로 실제 전송 경로의 디코딩 결과를 확인하세요.

## 첫 전송 테스트

복잡한 avatar를 동기화하기 전에:

1. `TSMPNetworkGameObjectToggle`을 추가합니다.
2. Local toggle을 확인합니다.
3. Texture path를 통한 delayed decoded toggle을 확인합니다.
4. Transform sync를 추가합니다.
5. 대역폭이 큰 sync component는 마지막에 추가합니다.

Toggle이 도착하지 않으면 더 많은 데이터를 추가하기 전에 transport와 decoder status를 먼저 해결하세요.

## 합격 기준 측정

실제로 사용할 전송 경로에서 알려진 송신 sequence와 payload 전체 바이트를 수신 결과와 비교하세요. 헤더 CRC와 `valid=yes`만으로 payload 정확성을 입증할 수 없습니다. 로컬 Spout 왕복, 압축 영상 왕복과 실제 VRChat 세션은 각각 검사합니다.

목표 Hz, 실제 발행 Hz, 적용 Hz, 송신 마감 누락, 수신 누락, 데이터 불일치, 원본 데이터 경과 시간과 p95 지연을 CPU/GPU 부하와 함께 기록하세요. 발행된 모든 프레임을 수신해도 송신 목표율을 달성했다는 뜻은 아닙니다. 지연과 업데이트율 예산을 시험 전에 정하고, 재시작·재연결·입퇴장과 목표 인원의 장시간 운전을 확인하세요.

RPC는 유한 반복 전송과 최근 32개 이벤트의 중복 방지 캐시를 사용합니다. 전달 ACK나 영구적인 exactly-once 보장은 없습니다. 같은 ID를 재사용하는 송신기 재시작과 오래된 영상 재등장을 애플리케이션 시험에 포함하세요. 로컬 루프백 성공이 미측정 외부 경로의 해당 동작까지 입증하지는 않습니다.

저장소의 `Validation~/VideoTransport` 검사는 유한한 오프라인 FFmpeg 영상 왕복 후 payload 전체 바이트를 비교하고, 거부되거나 달라진 프레임을 실패로 기록합니다. 실시간 OBS/Spout·네트워크·VRChat 검사와 별개이며, 한 압축 설정에서 통과해도 다른 설정이나 임의의 콘텐츠까지 보장하지는 않습니다.
