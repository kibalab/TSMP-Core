---
title: 개발자 테스트
---

# Developer testing

TSMP runtime code, network behaviour, codec을 변경할 때 이 체크리스트를 사용하세요.

테스트는 두 가지를 분리해서 봐야 합니다.

- Unity import와 UdonSharp compile 상태.
- 실제 texture path를 통한 runtime frame behaviour.

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
