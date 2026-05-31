---
title: TSMPDecodeCommon.cginc
---

# `TSMPDecodeCommon.cginc`

`TSMPDecodeCommon.cginc` は、TSMP コーデック デコード パスの共有シェーダー インクルードです。これは、共通のソース テクスチャ ユニフォーム、頂点入出力構造体、デフォルトの頂点関数、ブロック サンプリング ヘルパー、および YCoCg 変換ヘルパーを宣言します。

シェーダが TSMP ソース フレームをピクセルまたはブロックごとにサンプリングするときに、コーデック固有のデコード ロジックの前にこれを使用します。

インクルードパス:

```hlsl
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"
```

## テクスチャ入力

| シンボル | タイプ | 目的 |
| --- | --- | --- |
| `_MainTex` | `Texture2D` | ソース TSMP フレーム テクスチャ。 |
| `sampler_MainTex` | `SamplerState` | `_MainTex` で使用されるサンプラー。 |

デコード シェーダは、正確なバイト回復のためにポイントのようなサンプリングを使用する必要があります。ヘルパーは `SampleLevel(..., 0.0)` を呼び出して、ミップの選択を回避します。

## 共通制服

| ユニフォーム | 目的 |
| --- | --- |
| `_BlockSize` | 1 つのコーデック ブロックのピクセル サイズ。 |
| `_SampleSize` | ブロック内でサンプリングされたピクセルの数。設定されていない場合、ヘルパーは `_BlockSize` からデフォルトを選択します。 |
| `_StartBlock` | 最初のペイロード ブロック インデックス。 |
| `_ByteCount` | デコーダによって要求された有効なペイロード バイトの数。 |
| `_ActiveWidthBlocks` | 行あたりの書き込み可能/読み取り可能なブロックの数。 |
| `_SourceWidth` | ソースフレームの幅（ピクセル単位）。 |
| `_SourceHeight` | ソースフレームの高さ（ピクセル単位）。 |
| `_OutputWidth` | バイト出力テクスチャ幅 (ピクセル単位)。 |
| `_OutputHeight` | バイト出力テクスチャ高さ (ピクセル単位)。 |
| `_FlipY` | ゼロ以外の場合、ソース サンプリングを垂直方向に反転します。 |

これらの値は、デコーダ/マテリアル セットアップ パスによって割り当てられます。カスタム コーデックは、ユニフォームが利用可能な場合、テクスチャの寸法からそれらを推測すべきではありません。

## 頂点構造体

インクルードは以下を定義します。

```hlsl
struct appdata
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
};

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};
```

これらは、デコード パスに位置と UV のみが必要な場合に使用します。シェーダーにカスタムのインターポレーターが必要な場合は、このデフォルトの形状に依存するのではなく、別のパスで独自の構造体を定義します。

## `vert(appdata v)`

```hlsl
v2f vert(appdata v)
```

デフォルトの頂点関数。 `UnityObjectToClipPos(v.vertex)` でクリップスペースの位置を書き込み、`v.uv` を転送します。

## ピクセル サンプリング ヘルパー

### `SampleRgbAtTopLeftPixel(float2 pixel)`

指定されたソース ピクセルの中心で `_MainTex` をサンプリングし、RGB を返します。

Y 座標は `_FlipY` で計算されます。

```hlsl
float rawY = (pixel.y + 0.5) / _SourceHeight;
float uvY = lerp(rawY, 1.0 - rawY, _FlipY);
```

### `SampleLumaAtTopLeftPixel(float2 pixel)`

ピクセルで RGB をサンプリングし、平均輝度を返します。

```hlsl
dot(rgb, float3(0.33333334, 0.33333334, 0.33333334))
```

これは単純なチャネル平均であり、知覚的な輝度ではありません。

## ブロック サンプリング ヘルパー

### `SampleBlockRgb(float blockX, float blockY)`

ブロック内の中心にある正方形の領域をサンプリングし、平均 RGB を返します。

サンプルサイズのルール:

- `_SampleSize > 0.5` の場合は、`floor(_SampleSize)` を使用します。
- それ以外の場合は、`4` の場合は `_BlockSize >= 8` を使用し、小さいブロックの場合は `3` を使用します。
- サンプル サイズを `1..min(_BlockSize, 8)` に固定します。

ループの上限は `8x8` であるため、コーデックのサンプリングは予測可能です。

### `SampleBlockLuma(float blockX, float blockY)`

`SampleBlockRgb` と同​​じですが、平均輝度を返します。

### `SampleBlockByIndex(float blockIndex)`

`(blockX, blockY)` を使用してリニア ブロック インデックスを `_ActiveWidthBlocks` に変換し、`SampleBlockRgb` を呼び出します。

### `SampleLumaBlockByIndex(float blockIndex)`

線形ブロック インデックスを `(blockX, blockY)` に変換し、`SampleBlockLuma` を呼び出します。

## ペイロード ブロック ヘルパー

```hlsl
float PayloadBlockIndex(int symbolIndex)
```

戻り値:



これを使用して、ヘッダー/ペイロード境界をハードコーディングせずに、コーデック シンボル インデックスをペイロード ブロック インデックスにマッピングします。

## 整数ヘルパー



負でない値の場合は `floor(value / divisor)` を返します。バイト インデックスをシンボル インデックスにマッピングするときに使用します。

## 色変換ヘルパー



インクルードは互換性エイリアスも定義します。



マクロ/名前の競合を避けるために、可能な場合は新しいシェーダー コードで `TSMP_` 名を使用してください。

## `TSMPDecodeByteOutput.cginc`との共用

一般的なデコード シェーダは、サンプリング ヘルパーに `TSMPDecodeCommon.cginc` を使用し、`DecodeByte(int byteIndex)` を実装してから、バイト出力テクスチャを書き込むための `TSMPDecodeByteOutput.cginc` を組み込みます。



実際のコーデックは通常、ブロックごとに複数のビットまたはシンボルをデコードします。この例では、インクルード フローのみを示します。
