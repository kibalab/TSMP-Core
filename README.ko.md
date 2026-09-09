**한국어** | [English](README.en.md) | [日本語](README.md)

# TSMP Core

TSMP(Trans Sync Media Protocol)는 VRChat 월드에서 텍스처 스트림을 통해 네트워크 상태, RPC, 아바타 포즈, Animator, Timeline 같은 런타임 데이터를 전달하기 위한 오픈소스 패키지입니다.

Core 패키지는 TSMP를 씬에 배치하고 설정하는 기본 런타임입니다. 실제 픽셀 인코딩 방식은 코덱 패키지가 담당하며, 기본 사용에는 Luma4 코덱을 함께 설치하는 것을 권장합니다.

## 설치

VRChat Creator Companion에서 VPM 저장소를 추가합니다.

```text
https://vpm.kiba.red/
```

그 다음 `TSMP Core`와 `TSMP Codec Luma4`를 설치합니다.

## 빠른 시작

VRCSDK 없는 일반 Unity에서는 일반 Unity 지원이 포함된 Core와 Luma4를 UPM의 **Add package from disk**로 설치하세요. 두 환경 모두 아래의 같은 프리팹을 사용하며, 컴포넌트와 바인딩은 자동 준비됩니다. `Assets/TSMPGenerated` 리소스를 씬과 함께 관리하세요. Windows x64 Mono 구성을 검증했으며, 리플렉션과 stripping 제약은 설치 가이드를 참고하세요.

1. `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab`을 씬에 배치합니다.
2. 전송할 오브젝트에 필요한 `TSMPNetwork*` 컴포넌트를 추가합니다.
3. `TSMPSetup`에서 `Refresh Codecs`를 누르고 사용할 코덱을 선택합니다.
4. Setup에서 입출력과 코덱 설정을 확인합니다. 컴포넌트와 바인딩은 자동 갱신됩니다.
5. Encoder의 출력 RenderTexture를 송출하고, Decoder의 입력 RenderTexture에 같은 TSMP 화면을 넣습니다.

## 포함 기능

- TSMP Encoder / Decoder
- TSMPSetup 자동 구성 도구
- `[TransSync]` 필드 기반 상태 동기화
- `SendTransRPC(methodName, target)` 기반 TSMP RPC
- Transform, Rigidbody, Humanoid Pose, VRChat Avatar Pose, Animator, Timeline, BlendShape 동기화 컴포넌트
- 코덱 패키지 자동 검색 및 선택 UI
- 코덱 제작을 위한 공통 런타임, 셰이더 include, catalog asset 형식

## 문서

사용자 가이드와 개발자 문서는 아래에서 확인할 수 있습니다.

https://kibalab.github.io/TSMP-Core/

## 배포 상태

현재 TSMP는 beta 단계입니다. 패키지 버전과 Git 태그는 `v0.0.x-beta.x` 형식을 사용합니다.

## 라이선스

MIT License. Copyright (c) 2026 KIBA_Labs.
