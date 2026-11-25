---
title: Read TSMP data in a material
---

Use a received value to drive a material without copying it into a scene component. For example, a sender can transmit a float from 0 to 1 and the receiver can use it as brightness. The existing decoder still validates the image; you do not need an output component, a per-material RenderTexture or a CPU binding for the field.

## Check the channel first

1. Start with a working [receiver](../components/decoder.md). In Play Mode, check that its frame index advances. Sender bindings and the decoder's own input and payload textures are still needed for normal operation.
2. In the decoder's **Shader Output → Shader Channel**, select **Channel 0**. The default is **Off**. Give each simultaneously active decoder a different channel; any number of materials can read the same channel.
3. Create a `.shader` file with the following Built-in Render Pipeline shader. Create a material using **Example/TSMP Channel Monitor** and assign it to a visible mesh on the receiver. Do not add the material to the decoder or declare the channel textures in ShaderLab Properties.

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

**Magenta** means no frame is available on channel 0. **Green** means at least one frame was published, not that new frames are still arriving. If the picture stops updating, check the frame index.

## Use a TransSync field for brightness

Send a Float32 field whose value you can vary between 0 and 1. Replace the body of `frag` above with the code below. **13 and 0xFEDCBA98 are examples**: use **Copy Shader Lookup → Typed Fields** on the decoder to replace the entire `TSMPTryGetFloat(...)` expression with the one for your field. If the receiver has no CPU binding, the field won't be listed in that menu; use the sender's network ID and [wire hash](../scripting-api/shader-receive.md#field-key) instead.

```hlsl
float value;
if (!TSMPTryGetFloat(0u, 13u, 0xFEDCBA98u, value))
    return float4(1, 0, 1, 1);
float gain = saturate(value);
return float4(gain, gain, gain, 1);
```

Magenta means the field is unavailable or not a Float32. A received zero is valid and displays black. If SendOnChange omits the field in a later frame, the last brightness is retained. For packed byte arrays, use `TSMPTryGetBytes` and read the layout defined by the sender.

Use the [shader receive API](../scripting-api/shader-receive.md) for byte offsets, channel limits and reset behavior.

For a humanoid rig, follow [GPU skinning for a humanoid receiver](./gpu-skinning.md).
