---
title: 패키지 구조
---

# 패키지 구조

대부분의 사용자는 VPM 저장소에서 기본 `TSMP` 패키지를 설치하면 됩니다. 필요한 런타임과 기본 Luma4 코덱이 함께 들어옵니다.

## 기본 패키지

`com.kibalab.tsmp`는 VRChat 월드에 필요한 일반 TSMP 구성을 설치합니다.

새 프로젝트를 시작하거나 퀵스타트를 따라 할 때 이 패키지를 사용하세요. Core와 Luma4가 함께 설치되어 가장 실수가 적습니다.

## Core 패키지

`com.kibalab.tsmp.core`에는 씬에 추가하는 컴포넌트와 공유 런타임이 들어 있습니다.

- `TSMPSetup`
- `TSMPEncoder`
- `TSMPDecoder`
- `TSMPDebugCanvas`
- TSMP 네트워크 동기화 컴포넌트
- 바인딩 및 프로토콜 런타임
- 디버그 및 스트리밍 헬퍼
- Luma4가 사용하는 공유 코덱 베이스 코드

Core는 기본 TSMP 배포에서 표준 설정 경로인 Luma4 코덱 패키지와 함께 사용됩니다.

## Luma4 코덱 패키지

`com.kibalab.tsmp.codec.luma4`에는 기본 코덱, 머티리얼, 셰이더, 프리팹, 샘플이 들어 있습니다.

새 프로젝트라면 먼저 Luma4를 사용하세요. 기본 경로로 씬이 동작하는 것을 확인한 뒤 고급 텍스처 설정을 변경하는 것이 안전합니다.

## Samples

샘플은 별도 샘플 패키지가 아니라 관련 패키지 안에 포함됩니다. 주요 시작점은 다음입니다.

```text
Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab
```

TSMP가 패키지로 설치된 경우 위 프리팹 경로를 사용하세요.
