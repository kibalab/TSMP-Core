# 공용 Controller 자동 준비 및 Unity 지원 검증

검증일: 2026-09-09. 별도의 SDK-free 변환 메뉴를 없애고 같은 프리팹과 Setup 흐름을 사용하는 최종 구현의 결과입니다. 이전 단계의 `RESULTS.md`에 기록된 수동 변환 방식은 폐기했습니다.

## 작업 위치

- Core: `F:/Unity/TSMP/TSMP-Core-UnitySupport`, `fix/unity-without-vrcsdk`.
- Luma4: `F:/Unity/TSMP/TSMPCodec-Luma4-UnitySupport`, 같은 이름의 브랜치.
- 원본 저장소는 fetch/pull 후 최신 main을 확인했습니다. 기준은 Core `3cc081234461d0499cbfb06d7505ab6f1d7c0283`, Luma4 `cf1f8d4cb9e99c1a6090a3b719c4ba2fc9b4760f`입니다.
- 원본 작업 디렉터리의 사용자 변경은 보존했습니다. 검증은 별도 worktree에서 수행했고, 이후 아래 커밋으로 정리했습니다. 푸시와 배포는 하지 않았습니다.

### 관련 커밋

| 저장소 | 커밋 | 내용 |
| --- | --- | --- |
| Core | `8e52b6b` | 일반 Unity에서 SDK 의존성 분리 및 기존 native 실행 경로 유지. |
| Core | `f122f14` | 공용 Controller, 자동 준비, 리소스 분리 및 기존 프리팹 참조 보존. |
| Core | `b1a6719` | Unity/Player/Udon/SDK 월드 빌드와 공용 워크플로 검증 코드. |
| Luma4 | `99fcade` | SDK-neutral 코덱 프리팹과 SDK 선택적 패키지 설정. |

공용 Controller 구성에는 수정된 Core와 Luma4를 함께 사용해야 합니다. 위 커밋은 로컬 `fix/unity-without-vrcsdk` 브랜치에 있으며 공개 릴리스 버전을 의미하지 않습니다.

## 원인과 해결

수정 전 일반 Unity import는 먼저 UPM의 SDK 범위 의존성 값에서 실패했고, 메타데이터 수정 후에는 무조건 선언된 VRC/Udon 타입에서 C# 오류가 재현됐습니다. 실제 로그는 `01-before-import.log`, `02-before-code-fix.log`입니다. 기존 MonoBehaviour 경로를 유지하면서 SDK 타입, 플레이어 캡처, 프록시 및 Editor 코드만 조건부 분리했습니다.

이전 원본 Controller와 Luma4 프리팹에는 SDK backing 컴포넌트가 직렬화되어 있었습니다. SDK 없는 상태에서는 Missing Script가 되고, 단순히 backing만 제거한 프리팹 인스턴스는 SDK가 있는 환경에서 UdonSharp의 prefab 보호 검사에 걸렸습니다. 원본 프리팹을 환경마다 수정하거나 사용자가 변환 메뉴를 누르게 하지 않고 다음처럼 해결했습니다.

| 주요 파일 | 최종 변경 |
| --- | --- |
| `Runtime/Setup/TSMPSetup.cs` | OnValidate에서 구조를 즉시 변경하지 않고 지연 준비. 재진입 방지. persistent asset과 Prefab Stage에서는 원본 구조를 변경하지 않음. 코덱 소스의 실제 참조로 인스턴스 재사용 판단. |
| `Runtime/Setup/SetupInstantiation.cs` | Editor 준비 함수를 연결하는 작은 경계와 일반 Unity 런타임 fallback. 기존 인코더/디코더를 복제 구현하지 않음. |
| `Editor/Setup/SetupPreparation.cs` | 씬 배치, Hierarchy/설정 변경, Undo/Redo, 저장, Play 진입, Player/SDK 빌드 전에 자동 준비. 기존 Encoder/Decoder와 사용자 설정을 유지. 없는 컴포넌트만 생성. |
| `Editor/Setup/CodecInstancePreparation.cs` | 전체 코덱 계층을 복제한 뒤 공개 UdonSharp API로 필요한 backing 생성. Preset으로 사용자 필드를 유지하고 Udon 간 및 일반 Component에서 Udon으로 향하는 직렬화 참조를 재연결. SDK 내부 패치 없음. |
| `Runtime/Setup/SetupResources.cs`, `Editor/Setup/SetupResourceEditor.cs` | 패키지 텍스처/머티리얼을 Controller별 `Assets/TSMPGenerated` 하위 에셋으로 준비. 중복 생성 방지, 복제 시 리소스 분리, 중첩 Controller 소유권 구분. |
| `Samples/TSMPController.prefab` | SDK-neutral Setup과 자식 Transform만 가진 공용 템플릿. 필요한 컴포넌트와 코덱은 씬에서 자동 준비. SDK 유무와 무관하게 동일한 초기 직접 loopback 설정. |
| `Samples/Legacy/TSMPControllerLegacy.prefab` | 이전 Controller의 GUID와 component fileID, 기존 내용 유지. 기존 사용자 씬의 prefab 참조와 override를 끊지 않음. |
| Luma4 `Runtime/Codec_Luma4.prefab` | 코덱 컴포넌트의 GUID/fileID, 옵션과 머티리얼 참조는 유지하고 SDK backing만 템플릿에서 제거. |
| 설치/Quickstart/Setup 문서와 README | 영어/한국어/일본어에 동일한 사용 흐름, 자동 리소스 및 기존 씬 호환성 반영. |

프로토콜, codec ID, 패킷 구조, 보간 알고리즘은 바꾸지 않았습니다. Udon 컴파일이 만든 검증 프로젝트 전용 Program Asset 참조 변경은 최종 소스 변경에서 제외했습니다. 검증용 패키지 tarball에는 컴파일 당시의 생성 참조가 포함될 수 있지만, C# 소스와 Controller/codec 템플릿은 실제 수정본입니다.

## 설치와 사용 흐름

일반 Unity는 수정된 Core와 Luma4를 UPM local package로 설치합니다. 두 패키지를 실제 tarball로 만들어 Unity PackageCache에 설치하는 방식도 검증했습니다. SDK를 설치하거나 심볼을 수동으로 추가하지 않습니다. UPM dependencies에서 SDK 강제 의존성을 제거했고 VPM에는 Worlds `>=3.9.0` 의존성을 유지했습니다.

VRChat은 Worlds 프로젝트에서 같은 Core/Luma4 소스를 설치합니다. 이번 검증은 로컬 SDK 패키지와 작업 worktree를 참조했습니다. VCC 공개 저장소에 새 버전을 배포하거나 VCC 설치 UI를 자동 조작한 검증은 아닙니다.

양쪽 모두 `Samples/TSMPController.prefab`을 씬에 놓고 같은 Setup 인스펙터에서 참조를 지정합니다. 별도 변환 명령이나 필수 Apply Setup 단계가 없습니다. 코덱 검색은 켜진 상태로 유지됩니다. 외부 영상 수신 시 Decoder Source Texture를 지정하며, SDK 감지가 사용자 입력 텍스처나 코덱을 바꾸지 않습니다.

SDK 존재 감지는 asmdef versionDefines의 `com.vrchat.worlds >=3.9.0`으로 `UDONSHARP`를 정의합니다. 실제 Udon 컴파일러는 asmdef 심볼을 그대로 물려받지 않으므로 SDK 코드에 `UDONSHARP || COMPILER_UDONSHARP`, native 코드에 두 심볼의 부정을 사용합니다. Udon 컴파일 단계만의 분기는 별도로 유지합니다. Core 전체를 defineConstraints로 제외하지 않습니다. 없는 이름 기반 SDK assembly reference가 Unity 2022.3.22f1에서 import를 막지 않음을 실제 확인했으며, 이를 다른 버전 전체의 보장으로 일반화하지 않습니다.

## 검증 환경

- Unity 2022.3.22f1, revision `887be4894c44`, Windows Standalone 모듈.
- Core 0.1.0 수정본, Luma4 0.0.3-beta.2 수정본. 버전 번호는 올리지 않았습니다.
- VRCSDK base/worlds 3.10.4-beta.2 및 해당 SDK에 포함된 UdonSharp.
- NVIDIA GeForce RTX 4090, Direct3D11. `-nographics` 미사용.
- Player: Windows x64 Development, Mono, Managed Stripping Disabled, Run In Background.
- 일반 Unity 작업 폴더 참조 프로젝트: `F:/Unity/TSMP/Validation-NoSDK`.
- 일반 Unity tarball/PackageCache 프로젝트: `F:/Unity/TSMP/Validation-ReadOnly`.
- SDK 프로젝트: `F:/Unity/TSMP/Validation-VRC`.

## 검증 행렬

아래 로그와 결과 파일은 `F:/Unity/TSMP/Validation-Results` 기준입니다. `*-result.txt`는 성공한 assertion을 기록하며 같은 접두사의 `.log`가 전체 Unity/Player 로그입니다.

| 검증 | 결과 | 근거 |
| --- | --- | --- |
| SDK 없는 local package import, 공용 prefab 자동 준비 | 성공 | `20260909-204046-Workflow-result.txt` |
| SDK 프로젝트의 같은 prefab 자동 준비, 모든 backing과 Udon binding | 성공 | `20260909-204128-Workflow-result.txt` |
| SDK 없는 PackageCache 설치, 자동 준비 | 성공 | `20260909-204252-Workflow-result.txt`, Validation-ReadOnly의 manifest와 packages-lock.json의 local-tarball 항목 |
| 복제, Undo/Redo, 씬 저장/재열기, 사용자 설정과 prefab 연결 유지 | 성공 | 위 Workflow 3개 결과 |
| 여러 컴포넌트를 가진 코덱 계층과 직렬화 교차 참조 | 성공 | 위 Workflow 결과의 Plugin/Cross-Udon/Native component assertions |
| 중첩 Controller의 리소스/컴포넌트 분리 | 성공 | 위 Workflow 결과의 Nested/Parent assertions |
| 일반 Unity Editor 실제 GPU loopback | 성공 | local `20260909-201801-Play-result.txt`, PackageCache `20260909-203114-Play-result.txt` |
| SDK 없는 Setup 및 Network custom Inspector IMGUI | 성공 | `20260909-204538-Inspect-result.txt` |
| SDK 없는 PackageCache 소스로 Windows Player 빌드 | 성공 | `20260909-204608-Build.log`, BuildReport: Succeeded, Errors=0, Warnings=1, 92,923,071 bytes |
| 해당 Player 실제 실행과 GPU 송수신 | 성공 | `20260909-204628-Player-result.txt`, 6프레임 PASS |
| UdonSharp 전체 client 컴파일 | 성공 | `20260909-202447-Udon-result.txt`, 전체 20 scripts 완료, TSMP 13 programs 유효 bytecode 검사 |
| 최종 Editor 변경 후 SDK 로컬 world build | 성공 | `20260909-204402-World-result.txt`, 같은 로그에서 Udon 전체 20 scripts 컴파일 완료, 350,223-byte .vrcw 생성 |
| 영어/한국어/일본어 문서 production build | 성공 | `website-shared-workflow-build.log`, npm run build 종료 코드 0 |
| IL2CPP, stripping, 다른 플랫폼/그래픽 API | 미실행 | 이번 Mono/D3D11 검증과 구분 |
| 업로드된 VRChat 클라이언트 실행 | 미실행 | 업로드하지 않았으며 SDK 로컬 빌드를 클라이언트 동작 확인으로 간주하지 않음 |

루프백은 실제 Encoder -> Luma4 텍스처 -> GPU readback -> Decoder -> 수신 Component 경로입니다. Transform 위치/회전, 유효한 humanoid Avatar의 AnimationClip/Animator 기반 팔 회전과 Hips 위치, int/Unicode string TransSync, Remote RPC 1회 실행과 재전송 중복 억제를 확인했습니다. 마지막에는 검은 입력 이미지를 넣어 잘못된 헤더를 거부하고 변수값을 변경하지 않는지도 확인했습니다. 송수신 끝점을 한 씬에 넣기 위한 바인딩 대상 연결 외에는 수신 필드를 직접 대입하거나 디코더를 우회하지 않았습니다.

## 실행 방법과 산출물

실행기는 [Run-Validation.ps1](Run-Validation.ps1), 절차는 [README.md](README.md)에 있습니다. 검증 코드는 패키지 바깥 `Validation~`에 있으며 별도 프로젝트의 `Assets/Validation`에만 복사합니다. 제품 코드는 worktree 또는 그 소스로 만든 패키지 tarball을 그대로 참조합니다.

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
& './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-ReadOnly' -Results 'F:/Unity/TSMP/Validation-Results' -Step Workflow
& './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-ReadOnly' -Results 'F:/Unity/TSMP/Validation-Results' -Step Build
& './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-ReadOnly' -Results 'F:/Unity/TSMP/Validation-Results' -Step Player
& './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-VRC' -Results 'F:/Unity/TSMP/Validation-Results' -Step Udon
& './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-VRC' -Results 'F:/Unity/TSMP/Validation-Results' -Step World
```

- Player: `F:/Unity/TSMP/Validation-ReadOnly/Build/Mono/TSMPValidation.exe`. 같은 폴더의 DLL/Data 디렉터리도 필요합니다.
- BuildReport 요약: 같은 폴더의 `TSMPValidation.build-report.txt`.
- Native 검증 씬: `Validation-ReadOnly/Assets/Validation/Loopback.unity`, `SharedWorkflow.unity`.
- SDK 검증 씬: `Validation-VRC/Assets/Validation/World.unity`.
- SDK 보관 산출물: `F:/Unity/TSMP/Validation-Results/TSMP-shared-controller-world.vrcw`.
- World SHA256: `5D1377A47BB36F23DBB5BEBCC8B511DD1C3FE1FABB11FFF68477CC1E4ACFD316`.
- 최종 검증용 package tarball: `F:/Unity/TSMP/Validation-Results/final-packages`. npm pack 성공 자체를 Unity 빌드 성공으로 보고하지 않습니다.

## 한계와 호환성

- 새 공용 Controller는 씬 배치와 빌드 전 준비를 검증했습니다. 원본 템플릿을 Prefab Mode에서 열었을 때 구조를 자동 변경하지 않습니다. 런타임에 준비되지 않은 프리팹을 동적으로 로드/생성하는 SDK 경로까지 지원 확인한 것은 아닙니다.
- 이전 Controller GUID `727e518cd8c4092469c3319707c2c969`는 Legacy에 유지했습니다. 새 공용 Controller의 GUID는 `c533a143fe414dc48d6ff7b5bbc6486b`입니다. 이전 예제 씬과 사용자 참조는 Legacy를 계속 사용하므로 설정이 자동 교체되지 않습니다. SDK 없는 프로젝트로 이전하는 기존 씬은 SDK/Spout 등 다른 의존성을 별도로 검토해야 합니다.
- `Assets/TSMPGenerated`를 씬과 함께 보존해야 합니다. 사용자 에셋을 함부로 지우지 않도록 Controller 삭제 시 이 폴더를 자동 정리하지 않습니다.
- 검증 코덱은 실제 Luma4와 직렬화 참조 검사용 확장 계층입니다. 모든 제3자 코덱 프리팹의 Missing Script/RequireComponent 구조를 보장하지 않습니다. SDK 없는 사용을 제공하는 코덱은 SDK-neutral 템플릿을 제공해야 합니다.
- SDK 존재/부재의 별도 새 프로젝트는 검증했지만, 임의의 기존 씬에서 SDK 패키지를 제거했다가 재설치하는 모든 마이그레이션 조합을 실행한 것은 아닙니다.
- 기존 `TSMPDecodeCommon.cginc(104)`의 SampleBlockLuma 초기화 가능성 shader warning 1개는 남아 있습니다. 실제 GPU 송수신은 통과했습니다.
- 패키지 tarball을 교체한 직후 첫 Player 빌드 로그에는 Bee가 이전 PackageCache 경로로 컴파일을 시도한 CS2001이 있습니다. Unity가 그래프를 갱신해 해당 실행의 BuildReport도 성공했으며, 재실행한 최종 `204608-Build.log`에는 C# 컴파일 오류가 없습니다. SDK 로그의 라이선싱 클라이언트 서명/토큰 경고도 숨기지 않았으며 최종 단계의 실제 결과와 구분했습니다.
- npm은 설치된 Node 21.2.0과 npm 11.1.0 조합에 지원 버전 경고를 냈지만 문서 3개 언어 빌드는 성공했습니다. 이 작업에서 공용 Node 설치를 변경하지 않았습니다.
- UI 테스트는 실제 IMGUI 실행과 예외 검사입니다. 모든 인스펙터 폭의 스크린샷 기반 디자인 검증은 아닙니다.

기존 분석/초기 재현은 [RESULTS.md](RESULTS.md), 승인된 설계 검토는 [WORKFLOW-REVIEW.md](WORKFLOW-REVIEW.md)를 참고하세요. 둘의 이전 수동 변환 절차를 현재 사용법으로 적용하지 마세요.
