---
title: マテリアルで TSMP のデータを読む
---

送信側から 0～1 の数値を送り、受信側のマテリアルの明るさを変えてみましょう。画像の検証には既存のデコーダーを使います。出力用コンポーネント、マテリアルごとの RenderTexture、フィールドを受け取るための CPU バインディングは不要です。

## まずチャンネルを確認する

1. 動作している[受信側デコーダー](../components/decoder.md)を用意し、Play Mode でフレーム番号が進むことを確認します。送信フィールドのバインディングと、デコーダー自身の入力・ペイロードテクスチャは引き続き使用します。
2. デコーダーの **Reference → Shader Channel** で **Channel 0** を選びます。既定値は **Off** です。同時に動作するデコーダーには別々のチャンネルを割り当ててください。複数のマテリアルが同じチャンネルを読むことはできます。
3. 以下の例で `.shader` ファイルを作成します（Built-in Render Pipeline 用）。**Example/TSMP Channel Monitor** を使用するマテリアルを作り、受信側のシーンで見えるメッシュに割り当てます。デコーダーへのマテリアル登録や ShaderLab の Properties へのチャンネルテクスチャの宣言は不要です。

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

**マゼンタ**なら、チャンネル 0 にはまだ読めるフレームがありません。**緑**なら、少なくとも一度はフレームが公開されています。緑のままでも受信が続いているとは限りません。動きが止まったらフレーム番号を確認してください。

## TransSync フィールドで明るさを変える

送信側から 0～1 の範囲で変更できる Float32 フィールドを送ります。上の `frag` の本体を次のコードに差し替えてください。**13 と 0xFEDCBA98 は例です**。デコーダーの **Copy Shader Lookup → Fields** で実際のフィールドを選び、`TSMPTryGetField(...)` の式全体を置き換えます。受信側に CPU バインディングがないフィールドはこのメニューに表示されません。その場合は送信側のネットワーク ID と[通信で使用するハッシュ](../scripting-api/shader-receive.md#field-key)を直接指定します。

```hlsl
TSMPByteView view;
if (!TSMPTryGetField(0u, 13u, 0xFEDCBA98u, view))
    return float4(1, 0, 1, 1);
if (view.valueType != 3u || view.length != 4u)
    return float4(1, 1, 0, 1);

float gain = saturate(TSMPReadFloat32LE(view, 0u));
return float4(gain, gain, gain, 1);
```

マゼンタなら、そのフィールドはまだ受信されていません。黄色なら受信済みですが、型または長さが 4 バイトの Float32 と異なります。正しく設定できれば、送信値に合わせてメッシュの色が黒から白へ変わります。SendOnChange によって次のフレームでフィールドが省略されても、明るさは維持されます。パック済みのバイト配列の場合は、float として扱わず、そのフィールドのバイト配置に従って読み取ってください。

オフセット、チャンネルの容量、初期化条件は[シェーダー受信 API](../scripting-api/shader-receive.md)を参照してください。
