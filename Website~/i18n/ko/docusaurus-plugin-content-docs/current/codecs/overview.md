---
title: 코덱
---

# 코덱

코덱은 TSMP 데이터가 픽셀로 표현되는 방식을 결정합니다.

일반 사용자를 위해 Luma4 중심으로 단순하게 설명합니다. Luma4로 시작하고, 씬이 동작하는 것을 확인한 뒤 필요하면 텍스처 전송 경로를 조정하세요.

## Luma4

Luma4는 TSMP에 포함된 기본 코덱입니다. 텍스처 전송 경로를 바꿀 특별한 이유가 없다면 먼저 Luma4를 사용하세요.

Luma4가 좋은 시작점인 이유:

- 샘플 프리팹이 Luma4 기준으로 설정되어 있습니다.
- 문제 해결의 기준 경로입니다.
- 씬을 검증하는 동안 추가 패키지 결정을 줄여 줍니다.
- 사용자용 설정 가이드가 Luma4를 기준으로 작성되어 있습니다.

## Luma4 선택

1. `TSMPSetup`을 엽니다.
2. Codec 탭으로 이동합니다.
3. Luma4 handler를 선택합니다.
4. `Apply Setup`을 클릭합니다.

Sender와 receiver는 호환되는 Luma4 설정을 사용해야 합니다. Sender가 다른 codec으로 쓰고 receiver가 다른 codec으로 읽으면 header 또는 payload가 손상된 것처럼 보일 수 있습니다.

## Decode frame이 실패할 때

고급 설정을 바꾸기 전에 다음을 확인하세요.

- 인코딩 영역이 scale/filter되지 않았습니다.
- OBS 또는 camera output이 color correction을 적용하지 않습니다.
- Receiver가 올바른 input texture를 읽고 있습니다.
- Header 영역을 포함해 전체 TSMP frame이 보입니다.
- Header CRC error가 기록되지 않습니다.

Codec 탭에 Luma4가 보이지 않으면 씬 바인딩을 디버깅하기 전에 Luma4 패키지를 다시 설치하거나 refresh하세요.
