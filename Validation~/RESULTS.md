# VRCSDK 없는 Unity 지원 검증 결과

> 이전 단계의 기록입니다. 여기에 기록된 `Create Unity Controller` 메뉴와 환경별 프리팹 생성 방식은 제거되었습니다. 최종 공용 프리팹 구현, 설치 방법, 검증 결과는 [WORKFLOW-RESULTS.md](WORKFLOW-RESULTS.md)를 참조하세요.

검증일: 2026-09-09. 이 문서는 실제 실행 결과이며 패키지 ZIP 생성이나 배포 워크플로 결과가 아닙니다.

## 저장소와 작업 위치

- Core 원본: `F:/Unity/TSMP/TSMP-Core`, origin `https://github.com/kibalab/TSMP-Core.git`.
- `git fetch origin --prune`, `git pull --ff-only origin main` 실행. main은 이미 최신 `3cc081234461d0499cbfb06d7505ab6f1d7c0283`였습니다.
- 기존 Core 프로젝트 및 이전 `F:/Unity/K13A_ShaderLab/Assets/TSMP`의 dirty 변경은 보존했습니다. 적용 대상 경로와 조상 경로에서 AGENTS.md는 발견되지 않았습니다.
- Core 수정: `F:/Unity/TSMP/TSMP-Core-UnitySupport`, 브랜치 `fix/unity-without-vrcsdk`.
- Luma4도 pull 후 최신 `cf1f8d4cb9e99c1a6090a3b719c4ba2fc9b4760f`를 기준으로 `F:/Unity/TSMP/TSMPCodec-Luma4-UnitySupport`의 동명 작업 브랜치에 최소 수정을 분리했습니다.
- 이 초기 검증 당시에는 커밋 전 상태였습니다. 이후 정리한 커밋은 [WORKFLOW-RESULTS.md](WORKFLOW-RESULTS.md)에 기록했습니다. 푸시, 릴리스, 라이브 월드 업로드와 별도 기능 PR 병합은 하지 않았습니다.

## 재현한 원인

1. 최초에는 C#보다 먼저 UPM이 실패했습니다. Core와 Luma4의 UPM dependency `com.vrchat.worlds: >=3.9.0`을 Unity가 유효한 SemVer 값으로 인정하지 않았습니다. `01-before-import.log`에 두 패키지의 오류가 있습니다. 최초 Luma4 재현은 기존 Core 프로젝트에 있던 복사본을 사용했으며, 이후 수정과 최종 검증은 위의 최신 독립 Luma4 worktree를 사용했습니다.
2. package.json만 수정한 다음 import하자 SDK가 없는 프로젝트에서 실제 CS0246 등이 발생했습니다. 첫 오류는 `CodecBridge.cs(2,7): The type or namespace name 'VRC' could not be found`이며 BindingTable, TransSyncMetadata, ComponentReflection, Encoder, Decoder 등도 이어집니다. `02-before-code-fix.log`에 보관했습니다.
3. 실제 UdonSharp 컴파일에서는 asmdef versionDefines의 UDONSHARP가 컴파일러 자체에 전달되지 않았습니다. SDK의 `UdonSharpUtils.GetProjectDefines`는 프로젝트 심볼에 COMPILER_UDONSHARP를 추가하지만 어셈블리별 버전 심볼을 합치지 않습니다. 그 결과 Udon 컴파일이 native Encoder 경로를 읽는 문제가 재현됐습니다. 관련 조건을 두 심볼에 맞게 보완했습니다.
4. SDK 없는 환경에서 원본 Controller는 Missing Script 3개, 원본 Luma4 prefab은 1개였습니다. 새 Unity용 생성본은 원본을 덮어쓰지 않고 SDK backing component와 중첩된 원본 prefab 연결을 복사본에서 제거합니다.
5. prefab asset 로드 중 TSMPSetup.OnValidate가 persistent asset 아래에 codec instance를 생성하려 해 ArgumentException이 발생했습니다. ApplyNow와 editor tick에서 persistent asset을 제외했습니다. 씬 및 Prefab Mode의 편집은 유지합니다.
6. 기존 Controller의 명시적인 외부 입력 텍스처가 native 생성본에도 남으면 검은 텍스처에서 Header mismatch가 발생했습니다. Unity용 생성본의 초기값만 직접 texture loopback으로 설정했습니다. 기존 Setup 입력 우선순위와 원본 VRChat prefab은 변경하지 않았습니다.

## 주요 변경

| 영역 | 변경 |
| --- | --- |
| Core/Luma4 package.json | UPM에서는 SDK 강제 설치와 범위 형식 제거. Core는 Unity 모듈/UGUI만 의존, Luma4는 Core 0.1.0을 참조. VPM의 Worlds >=3.9.0과 기존 Core 범위 의존은 유지. |
| Runtime/Editor asmdef | 설치된 com.vrchat.worlds >=3.9.0에서 UDONSHARP 버전 심볼 정의. 어셈블리 이름과 GUID 유지. |
| TSMPBehaviour, Encoder/Decoder, Network 컴포넌트 | SDK 코드 선택은 UDONSHARP 또는 COMPILER_UDONSHARP. native 선택은 두 심볼 모두 없는 경우. 실제 Udon 컴파일 단계 전용의 COMPILER_UDONSHARP 검사는 그대로 유지. |
| BindingTable, TransSyncMetadata, ComponentReflection | Udon 타입/프록시 코드만 조건부 분리. 일반 Component 바인딩, 리플렉션, 값 적용/RPC 경로 유지. |
| CodecBridge, EncoderCodecRuntime, EncoderUdonBindingRuntime | SDK bridge 메서드와 타입만 조건부 컴파일. native codec 호출 유지. |
| TSMPNetworkVrchatAvatarPoseSync, VrchatAvatarPool | SDK 플레이어 조회/캡처를 분리하고 공용 풀 및 포즈 수신은 유지. SDK 없는 avatar pose 필드는 ReceiveOnly로 처리하여 캡처하지 못한 데이터를 재송신하지 않음. |
| InspectorUI, Network Editor, TransSyncBindingBuilder, SetupApplier | SDK 없는 상태에서 Udon 헤더/프록시 없이 일반 Inspector와 Setup/바인딩 생성 제공. |
| UnitySampleCreator | SDK 없는 Editor의 TSMP/Create Unity Controller 메뉴. Assets의 새 폴더로 Controller/codec/작업 RT 복사. 원본 prefab을 변경하지 않고 Missing SDK 스크립트 제거, 초기 직접 loopback 설정. |
| 문서 | 영어/한국어/일본어 설치·퀵스타트와 README의 설치 경로, 샘플, Player/stripping 제약 수정. |
| Validation~ | 재실행 가능한 Editor/Player/Udon/SDK 빌드 드라이버, 실제 GPU loopback 및 Inspector 검증. 런타임 패키지에 포함하지 않는 테스트 코드. |

Unity 2022.3.22f1에서 존재하지 않는 이름 기반 asmdef reference는 이번 일반 Unity import를 막지 않았습니다. 실제 import로 확인했으므로 SDK용 reference 목록을 제거하거나 가짜 SDK 어셈블리를 만들지 않았습니다. versionDefines가 reference를 선택 사항으로 만드는 기능이라고 해석하지 않습니다. 다른 Unity 버전에 대해 이 동작을 일반화하지 않습니다.

통신 포맷, codec ID, 패킷 구조, 보간 알고리즘, 사용자 스크립트 GUID는 변경하지 않았습니다. Udon 컴파일과 Setup 검증이 생성한 Program Asset/serialized program/material/sample texture 변경은 최종 소스 패치에서 제외했습니다.

## 환경

- Unity: **2022.3.22f1**, revision `887be4894c44`.
- 설치 확인: 2022.3.22f1, 2022.3.39f1, 2022.3.6f1 및 Unity 6 계열. 이번 검증은 전부 2022.3.22f1 사용.
- 빌드 모듈: 2022.3.22f1의 `windowsstandalonesupport` 확인.
- OS/타깃: Windows / StandaloneWindows64.
- 일반 Player: Development, Mono, Managed Stripping Disabled.
- 그래픽: NVIDIA GeForce RTX 4090, Direct3D11, 실제 AsyncGPUReadback 지원 확인. -nographics를 사용하지 않음.
- Core: 0.1.0의 위 수정본.
- Luma4: 0.0.3-beta.2의 최신 shader 수정 포함 + 이번 패키지 메타데이터 수정본.
- VRChat base/worlds: **3.10.4-beta.2**. 기존 프로젝트에서 별도 검증 프로젝트로 SDK 폴더를 복사하고 그 로컬 패키지를 사용.
- UdonSharp: 위 Worlds SDK에 동봉된 소스. 별도 패키지 semver는 발견되지 않아 임의 버전을 붙이지 않음. `UdonSharpCompilerV1.cs` SHA256: `7B49D8765AA8979DED059FDC956F8247FA2A7F82FEE04A4CF3A5D6ABC18740EA`.

일반 Unity 프로젝트: `F:/Unity/TSMP/Validation-NoSDK`. VRCSDK/UdonSharp 패키지와 Udon 사용자 심볼이 없으며 TSMPBehaviour의 직접 부모가 MonoBehaviour임을 테스트에서 확인했습니다. VRChat 프로젝트는 `F:/Unity/TSMP/Validation-VRC`로 분리했습니다. 두 프로젝트의 manifest는 실제 수정 worktree의 Core/Luma4 폴더를 file dependency로 참조합니다.

## 검증 행렬

로그 루트: **F:/Unity/TSMP/Validation-Results**. 아래 파일명은 이 폴더 기준입니다.

| 환경/단계 | 결과 | 근거 |
| --- | --- | --- |
| SDK 없는 새 프로젝트, 수정 전 | 실패 재현 | 01-before-import.log: UPM dependency 오류. 02-before-code-fix.log: 실제 C# SDK 타입 오류. |
| SDK 없는 프로젝트, 수정 후 import | 성공 | 03-after-guards.log 및 최종 Play/Build 단계, C# 오류 없음. |
| SDK 없는 Editor Setup 및 실제 prefab loopback | 성공 | 20260909-193929-Play-result.txt / 같은 이름 Play.log. 생성된 Controller를 씬에 배치하고 일반 바인딩 생성 후 송수신. |
| SDK 없는 Inspector IMGUI | 성공 | 20260909-194047-Inspect-result.txt / Inspect.log. Setup, Encoder, Decoder, Transform, Humanoid, 기본 NetworkBehaviour의 실제 OnInspectorGUI 실행. |
| Windows x64 Mono Player 빌드 | 성공 | 20260909-194008-Build.log, Build/Mono/TSMPValidation.build-report.txt. Errors=0, Warnings=1, Result=Succeeded, 92,915,512 bytes. |
| 해당 Player 실행 | 성공 | 20260909-194044-Player-result.txt / Player.log. 6프레임 전체 송수신 및 부정 테스트 PASS. |
| SDK 설치 프로젝트 C# import | 성공 | 05-vrc-import.log, SDK 초기 설정 후 17-udon-compile.log. |
| UdonSharp 전체 client 컴파일 | 성공 | 17-udon-result.txt / 17-udon-compile.log. 전체 20 scripts 완료, TSMP 13개 program에서 유효한 bytecode 검사, Setup backing Udon 바인딩 확인. |
| SDK의 로컬 월드 빌드/검증 | 성공 | 20-world-build-result.txt / 20-world-build.log. IVRCSdkWorldBuilderApi.Build의 검증·콜백 경유, Windows .vrcw 350,880 bytes 생성. |
| 문서 EN/KO/JA 프로덕션 빌드 | 성공 | website-build.log 및 website-build-final.log. npm ci 후 npm run build. |
| IL2CPP / stripping / 다른 플랫폼·그래픽 API | 미실행 | 이번 지원 확인은 Mono/stripping disabled/D3D11에 한정. |
| 업로드한 VRChat 클라이언트에서 실제 실행 | 미실행 | 요청 범위에 따라 업로드하지 않음. local world bundle 성공을 클라이언트 런타임 성공으로 대체하지 않음. |

### 동작 확인 방법

6프레임 모두 실제 Encoder → Luma4 shader texture → Decoder GPU readback → 일반 Component dispatcher → receiver의 순서를 거쳤습니다. 수신 필드에 테스트가 직접 값을 대입하거나 디코더를 우회하지 않았습니다.

- 움직이는 큐브의 position/rotation을 수신 큐브와 비교.
- 유효한 humanoid Avatar를 가진 Animator에 AnimationClip을 샘플링하고, 수신 리그의 arm rotation/hips position 갱신을 비교.
- TransSync int 및 변형 선택자를 포함한 Unicode 문자열 round-trip 비교.
- SendTransRPC(Remote)를 한 번 호출하고 sender 미실행 및 receiver 정확히 1회 실행 확인. 재전송 프레임의 중복 RPC 억제 포함.
- 마지막에 검은 텍스처를 넣어 유효하지 않은 프레임을 거부하고 기존 수신 변수값을 변경하지 않는지 확인. 이 한 단계의 의도된 Header magic mismatch LogError만 허용했습니다.
- 한 씬에서 송수신을 검증하기 위해 일반 Setup의 binding generation을 먼저 확인한 후 테스트 harness가 sender ID를 receiver Component로 연결했습니다. 원래 바인딩 API를 그대로 사용하며 production 코드의 ID 정책은 바꾸지 않았습니다.

## 산출물

- Player: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe` 및 같은 폴더의 UnityPlayer.dll/TSMPValidation_Data 등 전체 출력.
- BuildReport 요약: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.build-report.txt`.
- 검증 씬: `F:/Unity/TSMP/Validation-NoSDK/Assets/Validation/Loopback.unity`.
- 생성된 SDK-free Controller: 같은 프로젝트의 `Assets/Validation/UnitySample*` 폴더. 매 실행 새 폴더를 만들며 이전 결과를 덮어쓰지 않습니다.
- VRChat 검증 씬: `F:/Unity/TSMP/Validation-VRC/Assets/Validation/World.unity`.
- SDK 원본 빌드 출력: `C:/Users/kjh03/AppData/LocalLow/VRChat/VRChat/Worlds/scene-standalonewindows64-world-a21ba5cf-11af-4981-bea4-348424b30ad0.vrcw`.
- 보관한 world bundle: `F:/Unity/TSMP/Validation-Results/TSMP-validation-world.vrcw`, SHA256 `233379FA89DDE6E660A0B6AA8E76E08BCA8E06530797A213184CAD0E199FB1EA`.

## 실행 과정에서 구분한 실패

- 최초 SDK import는 SDK 폴더 복사가 끝나기 전에 실행하여 실패했습니다(04). 복사를 완료한 뒤 05에서 성공했습니다.
- 초기 검증 드라이버의 AddUdonSharpComponent 호출 위치를 SDK extension method에 맞게 고쳤습니다(07 → 10). 제품 코드의 컴파일 오류와 구분합니다.
- SDK의 UDON 등 자체 프로젝트 심볼 초기 설정 전에는 기본 scene template의 pipeline type이 로드되지 않았습니다(13). SDK의 EnvConfig.SetActiveSDKDefines 실행과 재시작 후 17에서 전체 컴파일·씬 설정 모두 성공했습니다.
- Windows Player를 batchmode로 실행한 첫 시도는 GPU readback을 제한 시간 내 완료하지 못했습니다(12). Run In Background와 일반 windowed Player 실행으로 실제 그래픽 경로를 검증했습니다. 단순 adapter 이름 확인만으로 성공 처리하지 않았습니다.
- 첫 SDK world build는 Unity/Bee named pipe가 이미 사용 중이라는 예외로 실패했습니다(18). SDK 설정 변경에 따른 컴파일이 정리된 다음 순차 재실행한 20에서 SDK 검증과 bundle 생성이 성공했습니다. 검증 우회나 다른 사용자 Unity 종료는 하지 않았습니다.
- 최초 전체 sample 기반 loopback은 기존 외부 입력 texture를 읽어서 실패했습니다(24). native 생성본의 초기 입력을 직접 loopback으로 바꾸고 최종 Play 및 Player에서 다시 통과시켰습니다.

## 한계와 마이그레이션

- Windows 빌드의 shader warning 1개: 기존 `TSMPDecodeCommon.cginc(104)` SampleBlockLuma의 potentially uninitialized variable 경고. 실제 Luma4 송수신은 통과했습니다. 이번 SDK 의존성 작업에서 shader/프로토콜 알고리즘을 임의 변경하지 않았습니다.
- IL2CPP와 managed stripping은 별도로 검증해야 합니다. reflection으로만 쓰는 필드/RPC 메서드를 보존해야 하며, Mono 통과를 IL2CPP 지원 근거로 사용하지 않습니다.
- VRChat 플레이어 캡처는 SDK에서만 가능합니다. native Animator/Humanoid 송수신은 검증했지만 실제 VRChat AvatarPool 스트림을 native Player로 수신하는 별도 세션은 실행하지 않았습니다.
- SDK 없는 환경에서는 UPM으로 **수정된 Core와 수정된 Luma4 모두** 설치합니다. VPM bundle은 Worlds SDK를 요구하는 기존 VRChat 설치 경로로 유지합니다. 이 로컬 변경은 아직 릴리스되지 않았으므로 공개 0.1.0에 이미 포함된 것으로 해석하면 안 됩니다.
- SDK-free Editor에서는 `TSMP > Create Unity Controller`로 생성한 Assets 복사본을 사용하세요. 원본 VRChat prefab/scene을 무조건 build에 포함하지 마세요. 생성본은 초기 직접 loopback이며 실제 영상 수신 시 Decoder Source Texture를 지정합니다.
- 생성본의 자동 codec 검색은 꺼져 있습니다. 다른 코덱으로 바꿀 때도 SDK-free prefab을 사용하세요. 원본 SDK-backed prefab을 다시 검색해 붙이지 않도록 주의합니다.
- Unity용 생성본은 원본과의 prefab 연결을 해제하므로 package update가 자동으로 덮어쓰지 않습니다. 필요 시 새 폴더로 재생성하고 사용자 설정을 옮기세요. 기존 VRChat 프로젝트에는 prefab GUID/타입 이름 변경이 없습니다.
- SDK 제거 시 남은 UDON/UDONSHARP 관련 사용자 심볼을 정리하세요. SDK 재설치 시 원본 VRChat controller를 사용하고 Udon 전체 컴파일 및 Apply Setup을 다시 실행하세요.
- 인스펙터 테스트는 실제 IMGUI 코드 실행 확인이며 화면 스크린샷에 의한 전체 UX 검수는 아닙니다.
- 최소 반복 실행 명령과 테스트 구성은 [README.md](README.md), 실행기는 [Run-Validation.ps1](Run-Validation.ps1)에 있습니다.

## 참고한 1차 자료

- [Unity 2022.3 Assembly Definition 설정](https://docs.unity3d.com/ja/2022.3/Manual/class-AssemblyDefinitionImporter.html)
- [Unity 2022.3 local path package](https://docs.unity.cn/2022.3/Documentation/Manual/upm-localpath.html)
- [UdonSharp Editor scripting](https://creators.vrchat.com/worlds/udon/udonsharp/editorscripting/)
- 실제 설치 SDK의 UdonSharpCompilerV1 / GetProjectDefines / EnvConfig / IVRCSdkWorldBuilderApi 및 VRCSdkControlPanelWorldBuilder 소스를 대조했습니다.
