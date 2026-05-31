# TSMP VRChat Implementation

상태: Draft  
대상: VRChat Worlds SDK + UdonSharp  
관련 문서: `TSMP_PROTOCOL.md`

## Runtime Flow

```text
TSMPNetworkBehaviour components
-> TSMPEncoder
-> block raster frame RenderTexture
-> video / capture / AVPro path
-> TSMPDecoder shader byte decode
-> VRCAsyncGPUReadback
-> generated binding table
-> TSMPNetworkBehaviour fields / RPC targets
```

TSMP의 현재 VRChat 구현은 generic networking layer를 기준으로 한다. `Transform`, `Animator`, `HumanoidPose`는 frame payload type이 아니라 `TSMPNetworkBehaviour` 파생 컴포넌트가 만드는 `VariableState` 또는 `RpcCall` 데이터다.

## Main Runtime Components

- `TSMPSetup`
  - scene-level encoder/decoder/video setup
  - resolution, block size, symbol mode, flipY, material assignment 관리
- `TSMPEncoder`
  - scene의 `TSMPNetworkBehaviour` 배열을 읽어 `NetworkFrame` payload 생성
  - payload를 selected symbol mode의 block raster frame으로 기록
- `TSMPDecoder`
  - source texture에서 header와 payload bytes를 shader로 복원
  - async GPU readback 결과를 `NetworkFrame` parser로 적용
- `TSMPNetworkBehaviour`
  - `[TransSync]` field와 RPC helper의 base class
- `TSMPNetworkTransformSync`
  - position/rotation/scale을 packed `RawBytes` 또는 typed values로 동기화
- `TSMPNetworkAnimatorSync`
  - animator parameter를 `VariableState`, trigger를 `RpcCall`로 동기화
- `TSMPNetworkHumanoidPoseSync`
  - humanoid pose를 packed `RawBytes` field로 동기화

## Editor Binding

`[TransSync]`는 Udon runtime reflection을 사용하지 않는다.

Editor 단계에서 `TransSyncBindingBuilder`가 scene을 스캔해 decoder에 flat binding arrays를 생성한다.

Binding entry:

- `NetworkId`
- `VariableHash`
- target behaviour
- field name
- value type
- direction
- priority

수동 실행:

```text
Tools -> TSMP -> Rebuild TransSync Bindings In Scene
```

자동 실행:

- hierarchy changed
- scripts reloaded

## Encoder Responsibilities

- `networkBehaviours` 수집
- `TSMPBeforeEncode()` hook 호출
- enabled `[TransSync]` field serialize
- queued RPC serialize
- `FrameHeader` 작성
- symbol raster frame 생성

Encoder는 직접 scene `Transform`을 알지 않는다. Transform sync는 `TSMPNetworkTransformSync`의 책임이다.

## Decoder Responsibilities

- source texture를 decoder material로 byte texture에 복원
- header에서 runtime decode options 적용
  - symbol mode
  - block size
  - active width blocks
  - payload size
  - decode sample size
- `NetworkFrame` payload validate
- generated binding table을 통해 field write
- RPC body parse와 dispatch

Decoder는 직접 scene `Transform`을 알지 않는다. Target field나 RPC는 binding table을 통해 찾는다.

## Byte Decoder Shader Framework

TSMP byte decoder shader는 공통 HLSL include를 사용한다.

- `com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc`
  - `_MainTex`, block/sample/output 공통 uniform 선언
  - `vert`, `appdata`, `v2f`
  - block 좌표 계산, RGB/Luma 샘플링, YCoCg helper
- `com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc`
  - decoded byte 4개를 `RGBA8` 스타일 output pixel로 packing하는 공통 fragment path

커스텀 디코더는 기존 material property 이름을 유지하고 다음 형태만 구현하면 된다.

```hlsl
#pragma target 3.5
#pragma vertex vert
#pragma fragment frag
#include "../../../com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"

int DecodeByte(int byteIndex)
{
    return 0;
}

#include "../../../com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

특수 출력 경로가 필요하면 `TSMP_SUPPRESS_DEFAULT_FRAG`를 정의한 뒤 `TSMPDecodeByteOutputFragment(i)`를 직접 호출하는 custom `frag`를 작성한다.

무거운 decode path는 shader import timeout을 줄이기 위해 codec package 내부의 별도 shader asset으로 분리한다.

`TSMPDecoder` does not know codec-specific modes or option layouts. It reads `codecId` and codec option bytes from the frame header, then asks the selected codec handler to choose the material and set codec-local shader uniforms.

Codec authoring assets are grouped under `com.kibalab.tsmp.codec.<codec>/Runtime/`:

- `Shaders/`
- `Shaders/cgincs/` for codec-local includes when needed
- `Materials/`
- `Scripts/`
- `Codec_<CodecName>.prefab`
- `TSMPCodecCatalog.asset`

`TSMPSetup` discovers `TSMPCodecCatalog.asset` files in the editor, references codec prefabs from those catalogs, instantiates selected codec prefabs into the scene, and flattens their Udon handlers into the small runtime table used by the decoder.

## Header Overrides

Decoder defaults `useHeaderPayloadLayout = true`. Each frame header can override runtime decode layout:

- `blockSize`
- `activeWidthBlocks`
- `payloadSize`
- `sampleSize`
- `codecId`
- up to 5 codec option bytes

`sourceWidth` and `sourceHeight` are not overridden by header. They come from `TSMPSetup` or the source texture.

Codec option bytes are owned and documented by the codec package.

## Shader Conventions

- Decoder shaders are named `Hidden/TSMP/...`.
- `_FlipY` is a runtime parameter and must match the video source orientation.
- Custom decoder shaders should implement `DecodeByte(int byteIndex)` unless they need a specialized output path.

## Default Profile

`TSMPSetup` should be the preferred place to edit resolution, block size, selected codec, and flipY.

Use the selected codec's capacity API to verify byte budget when changing resolution, symbol mode, or block size.

Encoder enforces:

```text
payloadBytes <= usablePayloadBytes
```
