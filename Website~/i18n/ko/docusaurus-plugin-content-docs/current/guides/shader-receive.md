---
title: 머티리얼에서 TSMP 데이터 읽기
---

송신 측에서 0~1 사이의 실수를 보내고, 수신 측 머티리얼의 밝기를 그 값으로 바꿔 봅니다. 이미지 검증은 기존 디코더가 담당합니다. 별도 출력 컴포넌트나 머티리얼별 RenderTexture를 만들거나 값을 받을 CPU 필드를 연결할 필요는 없습니다.

## 먼저 채널 확인하기

1. 작동 중인 [수신 디코더](../components/decoder.md)에서 시작합니다. Play Mode에서 프레임 번호가 증가하는지 확인하세요. 송신 필드의 바인딩과 디코더 자체의 입력·페이로드 텍스처는 그대로 사용합니다.
2. 디코더의 **Reference → Shader Channel**을 **Channel 0**으로 설정합니다. 기본값은 **Off**입니다. 동시에 켜는 디코더마다 다른 채널을 지정하세요. 여러 머티리얼이 같은 채널을 읽는 것은 괜찮습니다.
3. 아래 예제로 `.shader` 파일을 만듭니다(Built-in Render Pipeline용). **Example/TSMP Channel Monitor**를 사용하는 머티리얼을 만들고 수신 씬의 눈에 보이는 메시 하나에 적용하세요. 머티리얼을 디코더에 등록하거나 ShaderLab Properties에 채널 텍스처를 선언하지 않습니다.

```hlsl
Shader "Example/TSMP Channel Monitor"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Packages/com.kibalab.tsmp.core/Shaders/TSMP.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            float4 frag() : SV_Target
            {
                TSMPByteView view;
                return TSMPTryGetFrame(0u, view)
                    ? float4(0, 1, 0, 1) : float4(1, 0, 1, 1);
            }
            ENDCG
        }
    }
}
```

**자홍색**이면 채널 0에 아직 읽을 프레임이 없고, **초록색**이면 프레임이 한 번 이상 발행된 것입니다. 초록색이 유지된다고 해서 송신이 계속된다는 뜻은 아닙니다. 화면이 멈추면 프레임 번호를 확인하세요.

## TransSync 필드로 밝기 조절하기

송신 측에서 값을 0~1 사이로 바꿀 수 있는 Float32 필드를 보냅니다. 위 예제의 `frag` 본문을 아래 코드로 교체하세요. **13과 0xFEDCBA98은 예시 값**입니다. 디코더의 **Copy Shader Lookup → Fields**에서 실제 필드를 선택한 뒤 `TSMPTryGetField(...)` 식 전체를 교체하세요. 수신 측에 CPU 바인딩이 없으면 메뉴에 필드가 나오지 않습니다. 이 경우 송신자의 Network ID와 [전송용 필드 해시](../scripting-api/shader-receive.md#field-key)를 직접 지정하면 됩니다.

```hlsl
TSMPByteView view;
if (!TSMPTryGetField(0u, 13u, 0xFEDCBA98u, view))
    return float4(1, 0, 1, 1);
if (view.valueType != 3u || view.length != 4u)
    return float4(1, 1, 0, 1);

float gain = saturate(TSMPReadFloat32LE(view, 0u));
return float4(gain, gain, gain, 1);
```

자홍색이면 해당 필드를 아직 받지 못한 상태이고, 노란색이면 받았지만 4바이트 Float32가 아닙니다. 정상이라면 송신값을 바꿀 때 메시가 검은색에서 흰색으로 변합니다. 다음 프레임에서 SendOnChange로 필드가 생략되어도 밝기는 유지됩니다. 패킹된 바이트 배열을 읽을 때는 실수로 간주하지 말고 해당 필드의 바이트 배치에 맞춰 읽으세요.

오프셋, 채널 용량, 초기화 조건은 [셰이더 수신 API](../scripting-api/shader-receive.md)를 참고하세요.

휴머노이드 모델을 변형하려면 [GPU 스키닝 가이드](./gpu-skinning.md)를 참고하세요.
