Shader "KibaLab/TSMP/VRChat Support/Camera Output"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _BackgroundColor ("Background", Color) = (0, 0, 0, 1)
        _SourceAspect ("Source Aspect", Float) = 1.7777778
        _TargetAspect ("Target Aspect", Float) = 1.7777778
        _FitMode ("Fit Mode", Float) = 0
        _FlipY ("Flip Y", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Overlay+100" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Tint;
            float4 _BackgroundColor;
            float _SourceAspect;
            float _TargetAspect;
            float _FitMode;
            float _FlipY;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            float2 FitUv(float2 uv, out float outside)
            {
                float sourceAspect = max(_SourceAspect, 0.0001);
                float targetAspect = max(_TargetAspect, 0.0001);
                float2 p = uv - 0.5;
                outside = 0.0;

                if (_FitMode < 0.5)
                {
                    float2 content = float2(1.0, 1.0);
                    if (sourceAspect > targetAspect)
                        content.y = targetAspect / sourceAspect;
                    else
                        content.x = sourceAspect / targetAspect;

                    float2 halfContent = content * 0.5;
                    outside = step(halfContent.x, abs(p.x)) + step(halfContent.y, abs(p.y));
                    return p / content + 0.5;
                }

                if (_FitMode < 1.5)
                {
                    float2 scale = float2(1.0, 1.0);
                    if (sourceAspect > targetAspect)
                        scale.x = targetAspect / sourceAspect;
                    else
                        scale.y = sourceAspect / targetAspect;

                    return p * scale + 0.5;
                }

                return uv;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 screenUv = i.screenPos.xy / i.screenPos.w;
                screenUv = TRANSFORM_TEX(screenUv, _MainTex);

                float outside;
                float2 uv = FitUv(screenUv, outside);
                if (_FlipY > 0.5)
                    uv.y = 1.0 - uv.y;

                fixed4 color = tex2D(_MainTex, uv) * _Tint;
                return outside > 0.0 ? _BackgroundColor : color;
            }
            ENDCG
        }
    }
}
