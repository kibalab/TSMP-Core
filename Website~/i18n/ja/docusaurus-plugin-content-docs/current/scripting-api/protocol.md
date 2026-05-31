---
title: プロトコルAPI
---

# プロトコルAPI

名前空間: `K13A.TSMP`

これらのタイプは、テスト、ツール、カスタム コーデック、バリデーター、および診断に役立ちます。

## FrameHeader

定数:

| 絶え間ない | 価値 |
| --- | ---: |
| `Size` | `56` |
| `BytesBeforeCrc` | `52` |
| `CrcOffset` | `52` |
| `MaxCodecOptionBytes` | `5` |

重要な方法:

| 方法 | 使用 |
| --- | --- |
| `CreateDefault()` | デフォルトのネットワーク フレーム ヘッダーを作成します。 |
| `WriteTo(byte[] buffer, int offset)` | ヘッダーと CRC を書き込みます。 |
| `TryRead(...)` | ネイティブの読み取り/検証ヘルパー。 |
| `ClampDecodeSampleSize(...)` | クランプデコードサンプリングサイズ。 |

ヘッダー CRC は常に `WriteTo` によって書き込まれます。

## FrameHeaderReader

検証ステータス:

| 状態 | 意味 |
| --- | --- |
| `StatusOk` | ヘッダーは有効です。 |
| `StatusInvalidBuffer` | バッファ範囲が無効です。 |
| `StatusMagicMismatch` | 魔法は TSMP と一致しません。 |
| `StatusHeaderSizeMismatch` | ヘッダー サイズはサポートされていません。 |
| `StatusVersionMismatch` | メジャーバージョンはサポートされていません。 |
| `StatusCrcMismatch` | ヘッダー CRC が失敗しました。 |

エラー文字列を解析する代わりに、テストやツールでリーダー ステータス コードを使用します。

## プロトコル列挙型

| タイプ | 価値観 |
| --- | --- |
| `SymbolMode` | `Luma4` |
| `PayloadType` | `Empty`、`NetworkFrame` |
| `NetworkMessageType` | `VariableState`、`RpcCall` |
| `NetworkValueType` | Bool、Int32、Float32、ベクトル、四元数、UTF8 文字列、生のバイト、サポートされている配列。 |
| `NetworkSyncDirection` | `SendReceive`、`SendOnly`、`ReceiveOnly` |
| `RPCTarget` | `Local`、`Remote`、`All` |

## ネットワークフレームヘルパー

| タイプ | 使用 |
| --- | --- |
| `NetworkFrameProtocol` | ペイロードのオフセット、サイズ、定数。 |
| `NetworkFrameWriter` | ネットワーク フレームとメッセージ ヘッダーを書き込みます。 |
| `NetworkFrameReader` | ネットワークフレームとメッセージヘッダーを読み取ります。 |
| `NetworkValueCodec` | サポートされている C# 値の型を TSMP 値の型にマップします。 |
| `Binary` | リトルエンディアンの整数読み取り/書き込みヘルパー。 |
| `StableHash` | 安定した FNV-1a ハッシュ ヘルパー。 |
| `Crc32Runtime` | ランタイム CRC32 テーブルおよび計算ヘルパー。 |

## フレームヘルパー

| タイプ | 使用 |
| --- | --- |
| `FrameLayout` | 幅、高さ、ブロック サイズからアクティブなブロック レイアウトを計算します。 |
| `FrameCapacity` | 利用可能なフレーム/ペイロード容量を計算します。 |
| `FrameHeaderWriter` | ヘッダー書き込みヘルパー。 |

## ハッシュルール

TSMP は、フィールド キーと RPC メソッド名に安定したハッシュを使用します。送信者と受信者が確実に一致する必要がある場合は、ランタイムで生成された文字列をキーとして使用しないでください。
