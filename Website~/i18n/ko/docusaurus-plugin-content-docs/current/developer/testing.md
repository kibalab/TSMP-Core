---
title: 개발자 테스트
---

# Developer testing

TSMP runtime code, network behaviour, codec을 변경할 때 이 체크리스트를 사용하세요.

테스트는 두 가지를 분리해서 봐야 합니다.

- Unity import와 UdonSharp compile 상태.
- 실제 texture path를 통한 runtime frame behaviour.

## 재현 가능한 자동 검증

저장소 루트의 `Validation~/Run-Suite.ps1`을 사용합니다. 검사할 Core와 Luma4 소스를 설치한 전용 Native 프로젝트와 Worlds SDK 프로젝트를 따로 준비합니다.

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
& './Validation~/Run-Suite.ps1' -UnityEditor $unity -Project '<Native project>' -Results '<evidence root>' -Profile Native
& './Validation~/Run-Suite.ps1' -UnityEditor $unity -Project '<SDK project>' -Results '<evidence root>' -Profile Sdk
```

Suite는 프로젝트를 준비하고 원본 로그를 보존하며, 선행 검증 실패로 실행하지 못한 항목을 `NOTRUN`으로 기록합니다. 파싱이나 송신 스케줄링을 변경했다면 각 프로필에서 `-Steps FrameStructure,PayloadBudget`를 지정해 추가 실행합니다. 같은 진입점이 기존 전용 runner를 호출하고 SDK 프로젝트에서는 컴파일한 client VM을 검사합니다. 두 단계는 선택 실행이며, 기본 실제 texture loopback을 대체하지 않습니다.

현재 결과와 명령은 `Validation~/README.md`에서 찾을 수 있습니다. WorkloadFollowup, BindingCost, RpcBurst, Color256Followup, NextImprovements 보고서는 검사한 revision, 실패한 대조군과 검증 범위를 구분합니다. 장시간 측정과 소스를 교체하는 A/B controller는 별도 명령으로만 실행합니다. 유효 수신 갱신률, 원본 값의 나이, RPC 적용 지연을 기록하세요. 패킷 수락률만으로 유용한 전달을 판단하거나 서로 다른 실험의 개선율을 더하거나 Editor VM 시간을 실제 FPS로 환산하면 안 됩니다.

Editor/Player/VM 자동 검증만으로 새 VPM 다운로드 설치, Quest, 사람이 많은 VRChat 월드, 장시간 OBS–VRCDN 전송을 보증하지 않습니다. 출시 범위에 포함한다면 별도로 검사합니다. 개발 브랜치의 검증 결과가 배포 패키지에 동일한 변경이 들어갔다는 뜻도 아닙니다.

## Static checks

Project file이 있는 경우 Unity C# build도 실행합니다.

```powershell
dotnet build .\K13A.TSMP.Core.csproj --no-restore
dotnet build .\K13A.TSMP.Core.Editor.csproj --no-restore
```

이 검사는 Unity import나 UdonSharp compile을 대체하지 않지만, C# reference 문제를 빠르게 잡습니다.

## Unity checks

- Unity가 missing script 없이 import됩니다.
- UdonSharp compile이 성공합니다.
- `TSMPSetup`이 codecs를 refresh할 수 있습니다.
- `Apply Setup`이 bindings를 할당합니다.
- Sample prefab references가 유지됩니다.
- Encoder와 decoder inspector가 예상 diagnostics를 표시합니다.
- Codec 탭에 Luma4가 표시됩니다.

## Runtime smoke test

1. `TSMPController.prefab`을 배치합니다.
2. `TSMPNetworkGameObjectToggle`을 추가합니다.
3. Setup을 적용합니다.
4. Local interact가 즉시 toggle되는지 확인합니다.
5. Decoded RPC가 loopback delay 뒤 toggle되는지 확인합니다.
6. `TSMPNetworkTransformSync`를 추가합니다.
7. Payload bytes와 message count가 0보다 큰지 확인합니다.
8. Decoder가 값을 적용하는지 확인합니다.

이 테스트는 high-bandwidth pose data를 추가하기 전에 최소 end-to-end path를 증명합니다.

## Protocol tests

- Valid frame header가 CRC validation을 통과합니다.
- 변조된 header byte가 CRC validation에 실패합니다.
- 변조된 payload는 state/RPC 적용 전에 CRC validation에서 거부됩니다.
- CRC가 유효해도 뒤쪽 message 구조가 잘못되면 모든 state/RPC 적용 전에 거부하고, 수정한 event를 재전송하면 정상 처리됩니다.
- Empty `VariableState` message가 count되지 않습니다.
- RPC-only frame이 encode됩니다.
- Payload copy/FEC reference가 다시 들어오지 않습니다.
- Payload capacity boundary에서 oversized frame을 거부합니다.

## Codec tests

- Header region이 올바르게 decode됩니다.
- Payload capacity가 실제 writable data와 일치합니다.
- Payload start row가 decoder shader expectation과 일치합니다.
- Codec option bytes가 header를 통과합니다.
- Unused output area가 clear되거나 ignore됩니다.
- Target capture path를 통과한 뒤에도 decode가 동작합니다.

## Regression checklist

Release 전 확인:

- Core plus Luma4 default install.
- Editor-time encoding.
- Unity Play mode encoding/decoding.
- Uploaded VRChat runtime.
- Transport, codec, shader behaviour를 바꾼 release라면 OBS/Spout loopback.
