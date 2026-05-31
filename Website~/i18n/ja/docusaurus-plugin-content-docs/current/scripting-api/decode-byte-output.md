---
title: TSMPDecodeByteOutput.cginc
---

# `TSMPDecodeByteOutput.cginc`

`TSMPDecodeByteOutput.cginc` は、コーデック デコード パス用のシェーダー インクルードです。これは、バイトアドレス指定された `DecodeByte(index)` 関数を、デコードされたバイトを RGBA 出力テクスチャに書き込むフラグメント シェーダーに変換します。

カスタム コーデックがインデックスによって 1 バイトを回復でき、Core のデコーダ リードバック パスが標準のテクスチャ レイアウトでそれらのバイトを受信できるようにする場合に使用します。

インクルードパス:

```hlsl
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

## 必須の入力

インクルードでは、シェーダーがインクルードされる前、またはフラグメントが実行される前に、これらのシンボルがシェーダー内に存在することが予期されます。

| シンボル | タイプ | 目的 |
| --- | --- | --- |
| `v2f` | 構造体 | フラグメント入力タイプ。 `float2 uv` を公開する必要があります。 |
| `_OutputWidth` | ユニフォーム番号 | バイト出力テクスチャの幅 (ピクセル単位)。 |
| `_OutputHeight` | ユニフォーム番号 | バイト出力テクスチャの高さ (ピクセル単位)。 |
| `_ByteCount` | ユニフォーム番号 | デコーダによって要求された有効なデコードされたバイトの数。 |
| `DecodeByte(int byteIndex)` | 関数 | バイト インデックスに対してデコードされた 1 バイトを返します。 |

`_OutputWidth`、`_OutputHeight`、および `_ByteCount` は通常、デコーダまたはコーデック マテリアルのセットアップによって割り当てられます。

## `DecodeByte(int byteIndex)` 契約

デフォルトでは、インクルードは以下を呼び出します。

```hlsl
int DecodeByte(int byteIndex)
```

この関数は、`0` から `255` までの整数バイト値を返す必要があります。

ルール:

- `byteIndex` は、ゼロから始まるペイロード バイト インデックスです。
- この関数は、最後の RGBA ピクセル内の `_ByteCount` を超えるインデックスに対して呼び出される可能性があります。
- 範囲外の読み取りは `0` を返す必要があります。
- `0..255` の外側の値は、コーデックによってクランプまたは回避される必要があります。
- 関数は、正確なバイト値をフィルター処理されたサンプリングに依存すべきではありません。

## カスタムバイト関数名

別の関数名を使用するには、ファイルをインクルードする前に `TSMP_DECODE_BYTE_FUNC` を定義します。

```hlsl
int MyCodecDecodeByte(int byteIndex)
{
    return DecodeMySymbol(byteIndex);
}

#define TSMP_DECODE_BYTE_FUNC MyCodecDecodeByte
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

置換関数は同じ呼び出し形式を持つ必要があります。つまり、1 つの `int` インデックスが入力され、1 つのバイト形式の `int` が出力されます。

## 出力レイアウト

各出力ピクセルには 4 バイトが格納されます。

| チャネル | バイトインデックス |
| --- | --- |
| `R` | `baseByte + 0` |
| `G` | `baseByte + 1` |
| `B` | `baseByte + 2` |
| `A` | `baseByte + 3` |

基本インデックスは次のように計算されます。

```hlsl
baseByte = ((int)pixel.y * (int)_OutputWidth + (int)pixel.x) * 4;
```

返されるフラグメントの色は次のとおりです。

```hlsl
float4(b0 / 255.0, b1 / 255.0, b2 / 255.0, b3 / 255.0)
```

`baseByte >= _ByteCount` の場合、フラグメントは黒の透明なゼロを返します。

## `TSMPDecodeByteOutputFragment(v2f i)`

```hlsl
float4 TSMPDecodeByteOutputFragment(v2f i)
```

インクルードによってエクスポートされるメインのヘルパー関数。それ：

1. `i.uv` を出力ピクセル座標に変換します。
2. ピクセル座標を出力テクスチャ境界にクランプします。
3. そのピクセルの最初のバイト インデックスを計算します。
4. `TSMP_DECODE_BYTE_FUNC` を最大 4 バイト呼び出します。
5. それらのバイトを RGBA チャネルにパックします。

この関数は、シェーダに独自の `frag` エントリ ポイントがすでにある場合に使用します。

## デフォルト `frag(v2f i)`

抑制されない限り、インクルードは以下を定義します。

```hlsl
float4 frag(v2f i) : SV_Target
{
    return TSMPDecodeByteOutputFragment(i);
}
```

これは、パスがバイト出力テクスチャのみを書き込む単純なデコード マテリアルに便利です。

## `TSMP_SUPPRESS_DEFAULT_FRAG`

独自のフラグメント関数を提供する場合は、ファイルをインクルードする前に `TSMP_SUPPRESS_DEFAULT_FRAG` を定義します。

```hlsl
#define TSMP_SUPPRESS_DEFAULT_FRAG
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"

float4 frag(v2f i) : SV_Target
{
    return TSMPDecodeByteOutputFragment(i);
}
```

これは、パスに追加のデバッグ出力、条件付きロジック、または別のエントリ ポイント名が必要な場合に便利です。

## 最小限のシェーダーパスの例

```hlsl
struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float _OutputWidth;
float _OutputHeight;
float _ByteCount;

int DecodeByte(int byteIndex)
{
    return byteIndex & 255;
}

#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

この例では、予測可能なバイト パターンを出力テクスチャに書き込みます。実際のコーデックは、代わりに TSMP ソース フレームからデコードする必要があります。

## よくある間違い

| 症状 | 考えられる原因 |
| --- | --- |
| 出力はすべてゼロです | `_ByteCount` がゼロであるか、`DecodeByte` は常にゼロを返します。 |
| 最後のバイトにゴミが含まれています | `DecodeByte` は、ペイロード長を超えるインデックスを保護しません。 |
| 4 バイトごとに誤りがあります | RGBA チャネルの順序が出力レイアウトと一致しません。 |
| エディターでは動作するが、実行時には失敗する | シェーダーのプロパティ名またはマテリアルの設定は、ランタイム パスによって異なります。 |
| フレームが小さくなった後の古いバイト | 出力テクスチャはクリアされず、デコーダは `_ByteCount` を超える値を読み取ります。 |
