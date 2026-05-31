---
title: TSMPCodec
---

# TSMPCodec

名前空間: `K13A.TSMP`

コーデック ハンドラーの基本タイプ。

コーデック ハンドラーは、エンコーダーにペイロード バイトをピクセルに書き込む方法を指示し、デコーダーにペイロード バイトを回復するためにどのマテリアル/オプションを使用する必要があるかを指示します。

## パブリックフィールド

| 分野 | タイプ | 使用 |
| --- | --- | --- |
| `codecId` | `ushort` | フレームヘッダーに書き込まれる安定したコーデック ID。 |
| `displayName` | `string` | 編集者向けの名前。 |
| `codecOptionBytes` | `byte[]` | フレームヘッダーからコピーされたデコーダ側のオプションバイト。 |
| `selectedDecodeMaterial` | `Material` | バイトデコードに使用されるマテリアル。 |
| `payloadStartRow` | `int` | デコード用の最初のペイロード行。 |
| `payloadBlockCount` | `int` | デコードするペイロード ブロックの数。 |
| `byteCount` | `int` | 要求されたペイロードのバイト数。 |

ランタイム エンコーダーのクエリ結果フィールドは、Udon イベント ブリッジで使用するためにパブリックです。これらは `OnTSMPEncoderQuery()` と `OnTSMPEncoderWritePayload()` によって割り当てられます。

## ランタイムエンコードメソッド

| 方法 | 使用 |
| --- | --- |
| `GetEncoderSymbolMode()` | フレームヘッダーのシンボルモードを返します。 |
| `GetEncoderPayloadStartRow(width, blockSize)` | 最初のペイロード行を返します。 |
| `GetEncoderPayloadCapacityBytes(width, height, blockSize)` | ペイロードのバイト容量を返します。 |
| `GetEncoderCodecOptionByteCount()` | オプションのバイト数を返します (最大 5)。 |
| `GetEncoderCodecOptionByte(index)` | 1 つのオプション バイトを返します。 |
| `WriteEncoderPayload(...)` | ペイロード バイトをエンコーダ ピクセルに書き込みます。 |
| `ApplyDecodeOptions()` | ヘッダー/オプションからデコーダーの状態を適用します。 |

コーデックを VRChat 内で実行する必要がある場合、これらのメソッドは UdonSharp と互換性がある必要があります。

## ヘルパー メソッド

| 方法 | 使用 |
| --- | --- |
| `ReadCodecOptionByte(index, fallback)` | フォールバックを使用して 1 つのコーデック オプションを読み取ります。 |
| `ReadCodecOptionFlag(index, fallback)` | 1 つのオプションをブール値として読み取ります。 |
| `GetEncoderActiveWidthBlocks(width, blockSize)` | 書き込み可能なブロック幅を計算します。 |
| `GetEncoderActiveHeightBlocks(height, blockSize)` | 書き込み可能なブロックの高さを計算します。 |
| `WriteEncoderColorBlockAtIndex(...)` | 1 つのエンコードされたブロックを色で塗りつぶします。 |
| `ReadEncoderBits(...)` | ペイロードバイトから任意のビットを読み取ります。 |

## エディター/ネイティブ メソッド

`COMPILER_UDONSHARP` 以外でも利用可能:

| 方法 | 使用 |
| --- | --- |
| `SymbolMode` | ネイティブ シンボル モードのプロパティ。 |
| `TryWriteFrame(...)` | 完全なフレームを `Texture2D` に書き込みます。 |
| `GetCodecOptionBytes()` | ネイティブ コーデック オプションのバイト配列。 |
| `DecodeMaterialCount` | デコードマテリアルの数。 |
| `GetDecodeMaterial(index)` | マテリアル ルックアップをデコードします。 |
| `DebugMaterialCount` | デバッグマテリアルの数。 |
| `GetDebugMaterial(index)` | デバッグマテリアルのルックアップ。 |
| `ConfigureMaterials(context)` | デコード/デバッグマテリアルを設定します。 |

## うどんイベントブリッジ

`TSMPCodec` は、エンコーダーによって使用されるパブリック イベント メソッドを公開します。

| 方法 | 目的 |
| --- | --- |
| `OnTSMPEncoderQuery()` | エンコーダのクエリ結果フィールドに値を入力します。 |
| `OnTSMPEncoderWritePayload()` | `WriteEncoderPayload` を呼び出し、結果を保存します。 |

エンコーダはこのブリッジを使用するため、コーデック クラスをハードコーディングせずにオプションのコーデック パッケージを呼び出すことができます。
