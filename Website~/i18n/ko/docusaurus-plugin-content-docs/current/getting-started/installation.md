---
title: 설치
---

# 설치

일반 Unity 앱인지 VRChat 월드인지에 따라 설치 경로를 선택하세요. 두 환경에서 같은 컴포넌트 소스를 사용하므로 별도의 Standalone 브랜치는 필요하지 않습니다.

## 요구사항

- Unity 2022.3 LTS. 일반 Unity 검증에는 Windows의 Unity 2022.3.22f1을 사용했습니다.
- Core와 실제 코덱 패키지. 처음에는 Luma4를 사용하세요.
- 코덱 셰이더와 비동기 GPU readback을 지원하는 그래픽 장치.
- **VRChat 월드에서만** VRChat Worlds SDK 3.9.0 이상과 포함된 UdonSharp.

SDK가 없으면 컴포넌트는 MonoBehaviour로 동작합니다. Animator 기반 휴머노이드 캡처, Transform, TransSync 변수, RPC는 Unity Editor와 Windows Player에서 사용할 수 있습니다. VRChat 플레이어 정보의 취득에는 SDK가 필요하지만, 받은 포즈 패킷을 준비된 아바타 리그에 적용하는 작업은 플레이어 API가 필요하지 않습니다.

## VRChat: VPM

VRChat Creator Companion 또는 VPM 호환 매니저에 다음 저장소를 추가하세요.

```text
https://vpm.kiba.red/
```

**TSMP Core**와 **TSMP Codec Luma4**, 또는 둘을 포함한 TSMP 묶음을 설치하세요. 이 설치 경로의 VPM 메타데이터는 Worlds SDK 의존성을 유지합니다.

패키지 import와 SDK의 스크립트 컴파일이 끝나면 아래의 공용 컨트롤러를 사용하세요. TSMP가 Udon 컴포넌트와 바인딩을 자동으로 준비합니다.

## 일반 Unity: UPM

일반 Unity 지원 수정이 포함된 Core와 Luma4를 사용하세요. 이전 패키지에는 UPM 메타데이터에도 Worlds SDK 의존성이 남아 있을 수 있습니다. 이 경우 SDK 심볼만 삭제해도 해결되지 않습니다.

실제로 검증한 설치 방식은 Unity Package Manager의 **Add package from disk**입니다.

1. Core와 Luma4의 패키지 소스를 준비합니다.
2. Core의 `Packages/com.kibalab.tsmp.core/package.json`을 선택합니다.
3. Luma4의 `Packages/com.kibalab.tsmp.codec.luma4/package.json`을 선택합니다.
4. 패키지 해결과 스크립트 컴파일이 끝날 때까지 기다립니다.

프로젝트의 `Packages/manifest.json`에서 로컬 패키지 폴더를 지정해도 됩니다. 경로는 실제 위치에 맞추세요.

```json
{
  "dependencies": {
    "com.kibalab.tsmp.core": "file:../../TSMP-Core/Packages/com.kibalab.tsmp.core",
    "com.kibalab.tsmp.codec.luma4": "file:../../TSMPCodec-Luma4/Packages/com.kibalab.tsmp.codec.luma4"
  }
}
```

기존 dependencies에 항목을 추가하세요. manifest 전체를 덮어쓰지 마세요. UPM은 Unity 모듈 의존성만 설치하며 VRCSDK를 추가하지 않습니다. VPM의 SDK 요구사항과 UPM 의존성은 별개입니다.

SDK가 없는 프로젝트에 `UDONSHARP`나 `COMPILER_UDONSHARP`를 수동 정의하지 마세요. 기존 프로젝트에서 SDK를 제거했다면 남아 있는 SDK 관련 사용자 정의 심볼도 제거하세요.

## 컨트롤러 배치

일반 Unity와 VRChat 모두 `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab`을 씬에 드래그하세요. 변환 메뉴나 수동 Apply Setup 작업은 필요하지 않습니다. Setup이 필요한 컴포넌트를 생성하고 설치된 코덱을 자동으로 찾습니다.

공용 샘플은 처음에 로컬 루프백을 위해 Encoder 출력을 Decoder 입력으로 직접 연결합니다. 외부 스트림을 받을 때는 Decoder Source Texture를 지정하세요. SDK 유무에 따라 선택한 입력이나 코덱이 바뀌지 않습니다.

작업용 텍스처와 머티리얼은 `Assets/TSMPGenerated` 아래에 자동 준비됩니다. 씬과 함께 버전 관리에 포함하세요. 설치된 패키지 리소스를 수정하지 않으면서 Controller마다 독립적인 출력을 유지합니다.

패키지의 Udon Program Asset은 SDK 환경에서만 사용합니다. 공용 Controller는 SDK 없이 사용할 수 있지만, 다른 데모 씬은 아바타 에셋이나 스트리밍 플러그인, VRChat 컴포넌트 등 별도의 의존성이 필요할 수 있습니다.

## 기존 씬

기존 Controller 인스턴스의 프리팹 참조와 오버라이드를 유지하기 위해 이전 프리팹은 원래 GUID 그대로 `Samples/Legacy/TSMPControllerLegacy.prefab`에 보관합니다. 새 인스턴스에는 공용 `Samples/TSMPController.prefab`을 사용합니다. 기존 SDK 씬을 변환하는 명령은 필요하지 않습니다. 다만 이전 씬을 SDK 없는 프로젝트로 옮길 때는 공용 Controller를 사용하고 나머지 선택적 의존성도 확인하세요. Legacy 프리팹 자체가 SDK 없는 환경의 공용 샘플은 아닙니다.

## Player 설정

검증된 일반 Unity 구성은 **Windows x64, Mono, Managed Stripping 비활성화**입니다. 다른 앱에 포커스가 있어도 송수신을 계속하려면 **Run In Background**를 켜세요.

IL2CPP와 stripping은 해당 앱에서 별도로 검증해야 합니다. TransSync 필드 검색과 RPC 호출에 리플렉션을 사용하므로, stripping을 사용할 때는 리플렉션으로만 접근하는 필드와 메서드가 제거되지 않도록 보존해야 합니다. Mono 빌드 성공이 IL2CPP 성공을 의미하지는 않습니다.

다음으로 [퀵스타트](quickstart.md)를 진행하세요.
