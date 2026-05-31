---
title: カスタムコーデック
---

# カスタムコーデック

デフォルトの Luma4 テクスチャ表現がトランスポートのニーズと一致しない場合は、コーデックを作成します。

コーデック契約は単純です。

```text
Core provides header bytes and payload bytes.
The codec writes those bytes into pixels.
The decoder uses the codec to recover the same bytes.
```

シェーダー レベルの完全なチュートリアルについては、[コーデック実装ガイド](./codec-implementation.md) を参照してください。

## コア シェーダに含まれるもの

カスタム デコード シェーダは通常、2 つのコア cginc ファイルを使用します。

| 含む | 用途: | API |
| --- | --- | --- |
| `TSMPDecodeCommon.cginc` | 共有デコード ユニフォーム、`appdata`/`v2f`、頂点関数、ブロック サンプリング ヘルパー、輝度サンプリング、および YCoCg 変換ヘルパー。 | [スクリプト API: `TSMPDecodeCommon.cginc`](../scripting-api/decode-common.md) |
| `TSMPDecodeByteOutput.cginc` | バイトインデックス付きの `DecodeByte(index)` 関数を、デコーダによって読み取られた RGBA バイト出力テクスチャに変換します。 | [スクリプト API: `TSMPDecodeByteOutput.cginc`](../scripting-api/decode-byte-output.md) |

シェーダで共有 `TSMPDecodeCommon.cginc`、`v2f`、`_MainTex`、`_BlockSize`、`_SampleSize`、`_StartBlock`、`_ByteCount`、`_ActiveWidthBlocks`、`_SourceWidth`、`_SourceHeight`、`_OutputWidth`、または `_OutputHeight` が必要な場合は、最初に `_FlipY` を含めます。契約。コーデックのバイト リカバリ機能を実装した後、`TSMPDecodeByteOutput.cginc` を含めます。

## パッケージの形状

推奨されるパッケージ レイアウト:

```text
com.example.tsmp.codec.mycodec/
  package.json
  Runtime/
    Scripts/
    Shaders/
    Materials/
    Prefabs/
  Samples/
```

パッケージは以下に依存する必要があります。

```json
"com.kibalab.tsmp.core": "0.0.3-beta.1"
```

パッケージが VRChat ユーザーを対象としている場合は、VPM 依存関係メタデータも使用します。

## ランタイムクラス

`TSMPCodec` を拡張するコンポーネントを作成します。

```csharp
using K13A.TSMP;
using UnityEngine;

public sealed class TSMPCodecMyCodec : TSMPCodec
{
    private const int SymbolModeMyCodec = 128;

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
        return 0;
    }

    public override bool WriteEncoderPayload(
        Color32[] pixels,
        int width,
        int height,
        int blockSize,
        byte[] payloadBytes,
        int payloadByteCount)
    {
        return false;
    }

    public override void ApplyDecodeOptions()
    {
        selectedDecodeMaterial = byteDecodeMaterial;
    }

#if !COMPILER_UDONSHARP
    public override int SymbolMode => SymbolModeMyCodec;
#endif
}
```

このスケルトンは意図的に不完全です。実際のコーデックはペイロード バイトを書き込み、一致するデコード シェーダ/マテリアルの動作を提供する必要があります。

## 必要なエンコーダ メソッド

| 方法 | 要件 |
| --- | --- |
| `GetEncoderSymbolMode()` | フレームヘッダーに書き込まれたシンボルモードを返します。 |
| `GetEncoderPayloadStartRow(...)` | ペイロード ブロックがどこから始まるかを Core に伝えます。 |
| `GetEncoderPayloadCapacityBytes(...)` | 適合するペイロード バイト数を Core に伝えます。 |
| `WriteEncoderPayload(...)` | 提供されたピクセル バッファにペイロード バイトを書き込みます。 |

UdonSharp の互換性を維持するために、ロジックを単純にし、サポートされていない C# 構造を避けてください。

パッケージに安定したシンボル モード値を選択し、インストールされている他のコーデックとの衝突を回避します。コアのエンコーダーとデコーダーは、フレーム ヘッダーを通じてこの値を渡します。コーデック パッケージにはその意味があります。

## コーデックのオプション

コーデックは、フレーム ヘッダーで最大 5 つのオプション バイトを公開できます。

オーバーライド：



デコーダは、`codecOptionBytes` が呼び出される前に、`ApplyDecodeOptions()` を通じてこれらのバイトを受信します。

オプション バイトは、フレームをデコードするために必要な値にのみ使用します。エディタ専用の表示データには使用しないでください。

## デコーダーマテリアル

ネイティブ/エディターのデコードをサポートするには、Udon 以外のパス内でこれらをオーバーライドします。



実行時デコードの場合、`ApplyDecodeOptions()` は次のようなフィールドを設定する必要があります。

- `selectedDecodeMaterial`
- `payloadStartRow`
- `payloadBlockCount`
- シェーダに必要なコーデック固有の状態

## ネイティブエディターの作成

コーデックがエディター/ネイティブ C# パスにフレームを書き込む必要がある場合は、次を実装します。



UdonSharp が使用できない API を使用する場合は、このメソッドを `#if !COMPILER_UDONSHARP` 内に保持してください。

＃＃ 登録

`TSMPSetup` に掲載するには:

1. コーデック コンポーネントをプレハブに配置します。
2. 安定した `codecId` を設定します。
3. 読み取り可能な `displayName` を設定します。
4. `package.json` にパッケージのメタデータを含めます。
5. プレハブを参照する `TSMPCodecCatalog` を追加または生成します。
6. `TSMPSetup` でコーデックを更新します。

エンコーダはコーデック クラスを直接参照しないでください。 `TSMPCodec` API を介して検出して呼び出す必要があります。

## 検証チェックリスト

- エンコーダのペイロード容量は正しい。
- ヘッダー領域とペイロード領域は重なりません。
- コーデック オプション バイトはデコーダ状態に往復します。
- 出力は未使用領域をクリアします。
- デコーダーは破損したヘッダーを拒否します。
- コーデックは Unity Play モードおよび UdonSharp ランタイムで動作します。

## カスタム コーデックを作成する場合

カスタム コーデックは、デフォルトの Luma4 パスがトランスポート要件を満たせない場合にのみ作成します。問題がセットアップ、容量、または OBS フィルタリングにある場合、新しいコーデックでは問題は解決されません。
