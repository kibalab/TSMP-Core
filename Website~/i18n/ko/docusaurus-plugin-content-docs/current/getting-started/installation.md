---
title: 설치
---

# 설치

TSMP는 VRChat Creator Companion 또는 VPM 호환 패키지 매니저로 설치합니다.

일반적인 VRChat 월드 프로젝트에서는 VPM을 권장합니다. VRChat 도구가 기대하는 패키지 메타데이터까지 함께 설치되기 때문입니다.

## 요구사항

필수:

- Unity 2022.3 LTS.

사용 방식에 따라 필요한 선택 요구사항:

- VRChat 월드를 빌드하려면 VRChat Worlds SDK 3.9.0 이상.
- VRChat 런타임에서 TSMP를 사용하려면 UdonSharp.

VRChat SDK는 선택사항입니다. TSMP 패키지 에셋은 VRChat 월드가 아닌 프로젝트에서도 열 수 있습니다. UdonSharp Behaviour를 컴파일하거나 VRChat 월드를 업로드할 때 필요해집니다.

## VPM 저장소

다음 VPM 저장소를 추가하세요.

```text
https://vpm.kiba.red/
```

그 다음 설치할 패키지는 다음입니다.

```text
TSMP
```

기본 패키지는 Core와 Luma4 코덱을 설치합니다. Luma4는 권장 기본 코덱이며 샘플 컨트롤러 프리팹에서 사용됩니다.

## UPM 패키지 ID

Unity Package Manager로 설치하는 경우 같은 패키지 ID를 사용합니다.

```json
"com.kibalab.tsmp": "0.0.3-beta.3"
```

기본 패키지는 다음 패키지에 의존합니다.

```json
"com.kibalab.tsmp.core": "0.0.3-beta.3",
"com.kibalab.tsmp.codec.luma4": "0.0.3-beta.3"
```

특별한 패키지 관리 이유가 없다면 VRChat 프로젝트에서는 VPM을 사용하세요.

## 설치 후 확인

다음 항목이 있는지 확인하세요.

- `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab`
- Luma4 코덱 패키지.
- Add Component 메뉴의 `TSMPSetup`, `TSMPEncoder`, `TSMPDecoder`.

없다면 VCC 패키지 목록을 새로고침하고 TSMP 저장소가 올바르게 추가되었는지 확인하세요.
