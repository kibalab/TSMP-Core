---
title: コーデック実装ガイド
---

# コーデック実装ガイド

このガイドでは、C# ハンドラー、エンコーダー ロジック、デコード シェーダー、マテリアル、パッケージ メタデータ、検証など、コーデックの実装全体について説明します。

このページは[カスタムコーデック](./custom-codec.md)を読んでからご利用ください。そのページではパッケージレベルの契約について説明しています。このページでは、通常バグの原因となる実装の詳細について説明します。

## 実装順序

次の順序でコーデックを構築します。

1. 安定した `codecId` とシンボル モードを選択します。
2. 容量計算を実装します。
3. C# でペイロードの書き込みを実装します。
4. 書き込まれたピクセルからバイトを回復できるデコード シェーダーを作成します。
5. デコードマテリアルを設定します。
6. コーデック プレハブ/カタログを登録します。
7. 実際のシーン データを使用する前に、バイトごとの往復を検証します。

複雑なシェーダから始めないでください。まず、1 つの既知のフレームを介して小さなペイロードを往復させます。

## コーデック パイプライン

```text
TSMPEncoder
  -> builds header bytes and payload bytes
  -> queries TSMPCodec for symbol mode, start row, capacity, options
  -> passes payload bytes and pixel buffer to TSMPCodec
  -> TSMPCodec writes encoded pixels
  -> output texture is captured or transported

TSMPDecoder
  -> reads frame header
  -> validates CRC
  -> selects codec by codecId
  -> applies codec options
  -> runs codec decode material into a byte texture
  -> reads payload bytes and dispatches messages
```

エンコーダはコーデック固有のクラスを認識しません。選択された `TSMPCodec` をベース API 経由で呼び出します。これにより、オプションのコーデック パッケージがコアから独立した状態になります。

## パッケージレイアウト

ランタイム コード、シェーダー、マテリアル、プレハブ、サンプルをまとめたパッケージ レイアウトを使用します。

```text
com.example.tsmp.codec.mycodec/
  package.json
  Runtime/
    Scripts/
      TSMPCodecMyCodec.cs
    Shaders/
      TSMPDecodeMyCodecBytes.shader
      TSMPDebugMyCodecCalibration.shader
    Materials/
      GPUDecoderMyCodec.mat
    Prefabs/
      TSMPCodecMyCodec.prefab
  Samples/
```

パッケージはコアに依存する必要があります。

```json
"dependencies": {
  "com.kibalab.tsmp.core": "0.0.3-beta.2"
}
```

## 1. シンボル モデルを選択します

各エンコードされたブロックが伝送するビット数を定義します。

例:

| モデル | 意味 |
| --- | --- |
| 4ビットの輝度 | ペイロード バイトごとに 2 ブロック。 |
| 8ビットカラーインデックス | ペイロード バイトごとに 1 ブロック。 |
| NビットRGBシンボル | `ceil(payloadBits / N)` はブロックします。 |

選ぶ：

- 安定したシンボル モードの整数。
- 安定した `codecId`。
- コーデックにキャリブレーション ブロックが必要かどうか。
- コーデック オプションが必要かどうか。

フレーム ヘッダーには、最大 5 つのコーデック オプション バイトがあります。

リリース後も `codecId` とシンボル モードを安定させてください。いずれかの値を変更すると、そのコーデックのワイヤ形式が変更されます。

## 2. C# ハンドラーを実装する

`TSMPCodec` サブクラスを作成します。

```csharp
using K13A.TSMP;
using UnityEngine;

[AddComponentMenu("TSMP/Codecs/My Codec")]
public sealed class TSMPCodecMyCodec : TSMPCodec
{
    private const int SymbolModeMyCodec = 128;
    private const int BitsPerSymbol = 8;

    public Material byteDecodeMaterial;

    public override int GetEncoderSymbolMode()
    {
        return SymbolModeMyCodec;
    }

    public override int GetEncoderPayloadStartRow(int width, int blockSize)
    {
        return 5;
    }

    public override int GetEncoderPayloadCapacityBytes(int width, int height, int blockSize)
    {
        int activeWidthBlocks = GetEncoderActiveWidthBlocks(width, blockSize);
        int activeHeightBlocks = GetEncoderActiveHeightBlocks(height, blockSize);
        int payloadRows = Mathf.Max(0, activeHeightBlocks - GetEncoderPayloadStartRow(width, blockSize) - 1);
        return activeWidthBlocks * payloadRows * BitsPerSymbol / 8;
    }

    public override bool WriteEncoderPayload(
        Color32[] pixels,
        int width,
        int height,
        int blockSize,
        byte[] payloadBytes,
        int payloadByteCount)
    {
        int activeWidthBlocks = GetEncoderActiveWidthBlocks(width, blockSize);
        int payloadStartBlock = GetEncoderPayloadStartRow(width, blockSize) * activeWidthBlocks;

        for (int i = 0; i < payloadByteCount; i++)
            WriteEncoderColorBlockAtIndex(pixels, width, height, blockSize, payloadStartBlock + i, ByteToColor(payloadBytes[i]));

        return true;
    }

    public override void ApplyDecodeOptions()
    {
        selectedDecodeMaterial = byteDecodeMaterial;
        payloadStartRow = GetEncoderPayloadStartRow(0, 8);
        payloadBlockCount = byteCount;
    }

    private static Color32 ByteToColor(byte value)
    {
        return new Color32(value, value, value, 255);
    }

#if !COMPILER_UDONSHARP
    public override int SymbolMode => SymbolModeMyCodec;
    public override int DecodeMaterialCount => byteDecodeMaterial != null ? 1 : 0;
    public override Material GetDecodeMaterial(int index) => index == 0 ? byteDecodeMaterial : null;
#endif
}
```

ランタイム パスは UdonSharp と互換性のあるものにしてください。ジェネリック メソッド、LINQ、リフレクション、およびサポートされていない Unity API は避けてください。

## 3. エンコーダロジックの書き込み

エンコーダ側のメソッドは `WriteEncoderPayload` です。

受け取ります:

| パラメータ | 意味 |
| --- | --- |
| `pixels` | 書き込み可能な出力ピクセルまたはブロック バッファー。 |
| `width`、`height` | 出力フレームの寸法。 |
| `blockSize` | 1 つのエンコードされたブロックのサイズ。 |
| `payloadBytes` | ペイロードバッファ。 |
| `payloadByteCount` | 有効なペイロードのバイト数。 |

ルール:

- アクティブフレーム領域の外側に書き込まないでください。
- ヘッダー/ベース領域をそのまま維持します。
- コーデックが所有するペイロード領域のみを書き込みます。
- 必要な状態が欠落している場合は、`false` を返します。
- 未使用の容量を一貫してクリアまたは無視します。

`TSMPCodec` の便利なヘルパー:

- `GetEncoderActiveWidthBlocks`
- `GetEncoderActiveHeightBlocks`
- `WriteEncoderColorBlockAtIndex`
- `ReadEncoderBits`

### 容量はシェーダーと一致する必要があります

最も一般的なコーデックのバグは、次の 3 つの値の不一致です。

- エンコーダのペイロード容量。
- エンコーダペイロード開始ブロック。
- デコーダ シェーダ `_StartBlock` および `_ByteCount`。

これらのいずれかが一致しない場合、ペイロード メッセージが破損または欠落していても、ヘッダーは正しくデコードできます。

## 4. コーデック オプションを追加する

ビット深度、リファインメント モード、キャリブレーション モードなどのデコードの選択には、コーデック オプション バイトを使用します。

```csharp
public override int GetEncoderCodecOptionByteCount()
{
    return 2;
}

public override int GetEncoderCodecOptionByte(int index)
{
    if (index == 0)
        return mode;
    if (index == 1)
        return refine ? 1 : 0;

    return 0;
}
```

デコード時に、`ApplyDecodeOptions()` で読み取ります。

```csharp
public override void ApplyDecodeOptions()
{
    int mode = ReadCodecOptionByte(0, 0);
    bool refine = ReadCodecOptionFlag(1, false);
}
```

## 5. デコードシェーダーを作成する

デコード シェーダは、エンコードされたピクセルをバイト ピクセルに変換し直します。出力は、各 RGBA ピクセルがデコードされた 4 つのバイトを表すバイト テクスチャです。

シェーダーコントラクトは次のとおりです。

```text
DecodeByte(byteIndex) -> integer 0..255
```

`TSMPDecodeByteOutput.cginc` は、デコードされた 4 バイトを 1 つの出力ピクセルにパッキングする処理を行います。シェーダで実装する必要があるのは、コーデックのシンボル表現に対するバイト リカバリだけです。

この構造から始めます。

```hlsl
Shader "Hidden/TSMP/Decode MyCodec Bytes"
{
    Properties
    {
        _MainTex ("TSMP Source", 2D) = "black" {}
        _BlockSize ("Block Size", Float) = 8
        _SampleSize ("Sample Size", Float) = 0
        _StartBlock ("Start Block", Float) = 0
        _ByteCount ("Byte Count", Float) = 0
        _ActiveWidthBlocks ("Active Width Blocks", Float) = 80
        _SourceWidth ("Source Width", Float) = 640
        _SourceHeight ("Source Height", Float) = 360
        _OutputWidth ("Output Width", Float) = 14
        _OutputHeight ("Output Height", Float) = 1
        _FlipY ("Flip Y", Float) = 1
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"

            int DecodeByte(int byteIndex)
            {
                if (byteIndex < 0 || byteIndex >= (int)_ByteCount)
                    return 0;

                float blockIndex = _StartBlock + byteIndex;
                float3 rgb = SampleBlockByIndex(blockIndex);
                return (int)round(saturate(rgb.r) * 255.0);
            }

            #include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
            ENDCG
        }
    }

    Fallback Off
}
```

`TSMPDecodeCommon.cginc` は以下を提供します:

- ソース テクスチャのサンプリング。
- ブロックサンプリング。
- `PayloadBlockIndex`。
- YCoCg ヘルパー。
- フリップ Y 処理。

`TSMPDecodeByteOutput.cginc` は、出力ピクセルごとに `DecodeByte(byteIndex)` を 4 回呼び出し、RGBA バイト テクスチャを書き込みます。

### パスを含める

共有 TSMP シェーダ ファイルのパッケージ インクルード パスを使用します。これにより、コーデック シェーダがプロジェクト `Assets` レイアウトから独立した状態になります。

### シェーダーのプロパティ

デコード マテリアルは、`TSMPCodec.ConfigureDecodeMaterial` で使用される標準プロパティをサポートする必要があります。

| 財産 | 意味 |
| --- | --- |
| `_MainTex` | ソース TSMP テクスチャ。 |
| `_BlockSize` | エンコードされたブロック サイズ (ピクセル単位)。 |
| `_SampleSize` | ヘッダー/セットアップからサンプル サイズをデコードします。 |
| `_StartBlock` | デコードする最初のペイロード ブロック。 |
| `_ByteCount` | 要求されたペイロードのバイト数。 |
| `_ActiveWidthBlocks` | フレームのアクティブなブロック幅。 |
| `_SourceWidth` / `_SourceHeight` | ソースフレームの寸法。 |
| `_OutputWidth` / `_OutputHeight` | バイトテクスチャの寸法。 |
| `_FlipY` | ソースサンプリングを垂直方向に反転するかどうか。 |

## 6. 必要に応じてキャリブレーションを追加します

トランスポートの色が変わる場合は、ペイロード ブロックの前にキャリブレーション シンボルを追加します。

典型的なパターン:

1. エンコーダは既知のシンボル テーブルを書き込みます。
2. デコード シェーダがキャリブレーション テーブルをサンプリングします。
3. デコード シェーダは、最も近いキャリブレーション エントリによってペイロード ブロックを分類します。

必要に応じて、材料プロパティを通じてキャリブレーション開始ブロックを公開します。エディタ/ネイティブ パスには `ConfigureMaterials(CodecMaterialContext context)` を、ランタイム パスには `ApplyDecodeOptions()` を使用します。

## 7. マテリアルを構成する

各デコード シェーダのマテリアルを作成し、それをコーデック コンポーネントに割り当てます。

ネイティブ/エディターマテリアル構成の場合:

```csharp
#if !COMPILER_UDONSHARP
public override int DecodeMaterialCount => byteDecodeMaterial != null ? 1 : 0;

public override Material GetDecodeMaterial(int index)
{
    return index == 0 ? byteDecodeMaterial : null;
}

public override void ConfigureMaterials(CodecMaterialContext context)
{
    base.ConfigureMaterials(context);
    SetFloatIfPresent(byteDecodeMaterial, "_MyCalibrationStartBlock", Luma4Raster.PayloadStartRow * context.FrameLayout.ActiveWidthBlocks);
}
#endif
```

ランタイム デコードの場合、`ApplyDecodeOptions()` は選択したマテリアルとランタイム フィールドを設定する必要があります。

```csharp
public override void ApplyDecodeOptions()
{
    selectedDecodeMaterial = byteDecodeMaterial;
    payloadStartRow = 5;
    payloadBlockCount = byteCount;
}
```

## 8. エディタ/ネイティブフレーム書き込みの実装

コーデックがエディター ツールまたはネイティブ C# パスでエンコードする必要がある場合は、`TryWriteFrame` を実装します。

```csharp
#if !COMPILER_UDONSHARP
public override bool TryWriteFrame(Texture2D texture, int blockSize, byte[] headerBytes, byte[] payloadBytes, out string error)
{
    if (!ValidateRasterFrame(texture, blockSize, headerBytes, payloadBytes, out error))
        return false;

    Color32[] pixels = FrameRaster.CreateClearedPixels(texture.width, texture.height);
    Luma4Raster.WriteBaseRegions(pixels, texture.width, texture.height, blockSize, headerBytes);
    WriteEncoderPayload(pixels, texture.width, texture.height, blockSize, payloadBytes, payloadBytes.Length);
    FrameRaster.WriteEndMarker(pixels, texture.width, texture.height, blockSize);
    texture.SetPixels32(pixels);
    texture.Apply(false, false);
    return true;
}
#endif
```

このパスは UdonSharp ランタイム エンコーディングとは別のものですが、同じフレーム レイアウトを生成する必要があります。

## 9. コーデックを登録する

`TSMPSetup` に表示するには:

1. コーデック コンポーネントを使用してプレハブを作成します。
2. `codecId`、`displayName`、マテリアル、オプションを割り当てます。
3. プレハブを参照する `TSMPCodecCatalog` アセットを追加します。
4. パッケージのメタデータを `package.json` に配置します。
5. `TSMPSetup` でコーデックを更新します。

エンコーダはコーデック タイプをハードコーディングしないでください。 `TSMPCodec` を通じて呼び出す必要があります。

## 10. コーデックを検証する

最低限の検証:

- ヘッダー バイトは正しくデコードされます。
- ペイロードバイトは正確に往復します。
- ペイロード容量の計算は実際の書き込み可能容量と一致します。
- ペイロード開始行はシェーダ `_StartBlock` と一致します。
- コーデック オプション バイトがデコーダに到着します。
- ヘッダー ピクセルが破損している場合、CRC 不一致が報告されます。
- ペイロードが縮小すると出力はクリアされます。
- Unity エディターのパスと UdonSharp ランタイム パスは互換性のあるフレームを生成します。

推奨される最初のペイロード:

```text
00 01 02 03 04 05 06 07 08 09 FE FF
```

これにより、実際の TSMP ペイロードをテストする前に、バイト オーダー、クランプ、ゼロ処理、高値処理が捕捉されます。

## 一般的な障害モード

| 症状 | 考えられる原因 |
| --- | --- |
| ヘッダーは有効ですが、ペイロードが無効です | ペイロードの開始行またはバイト容量が一致しません。 |
| CRC の不一致 | ヘッダー領域が破損しているか、シェーダーが間違ったピクセルを読み取っています。 |
| エディターでは動作するが、Udon では失敗する | ランタイム パスはサポートされていない API を使用しているか、ネイティブ パスと異なります。 |
| 古いデータは引き続き表示されます | 出力のクリアまたは未使用領域の処理が不完全です。 |
| デコーダーが読み取るバイト数が少なすぎます | `payloadBlockCount` または `_ByteCount` は間違っています。 |
| セットアップにコーデックが表示されない | カタログ、プレハブ参照、またはパッケージのメタデータが欠落しています。 |
