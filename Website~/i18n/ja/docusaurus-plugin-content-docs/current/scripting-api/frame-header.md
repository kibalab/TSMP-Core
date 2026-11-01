---
title: FrameHeader API
---

# `FrameHeader`

`FrameHeader` は、各 TSMP フレームの先頭にある固定の 56 バイトのヘッダーを記述します。これにより、デコーダはペイロード バイトを読み取る前に、プロトコル バージョン、コーデック、ペイロード サイズ、フレーム インデックス、レイアウト メタデータを識別できます。

## ヘッダーの検証

ヘッダー リーダーは以下を検証します。

- TSMP 魔法。
- ヘッダーのサイズ。
- プロトコルのメジャー バージョン。
- ペイロードのサイズとレイアウト。
- ヘッダー CRC32。

CRC 検証が失敗した場合、デコーダはフレームを破棄し、警告をログに記録します。ペイロード バイトは、失敗したヘッダーからはデコードされません。

## 重要な分野

| 分野 | 目的 |
| --- | --- |
| `Magic` | フレームを TSMP として識別します。 |
| `HeaderSize` | ヘッダーのバイト長を固定しました。 |
| `VersionMajor` / `VersionMinor` | プロトコルの互換性。 |
| `FrameIndex` | 送信側フレームカウンター。 |
| `PayloadSize` | デコードするペイロードのバイト数。 |
| `PayloadType` | ペイロードの解釈。ネットワーク フレームは、TSMP ネットワーク ペイロード タイプを使用します。 |
| `CodecId` | コーデックハンドラーを選択します。 |
| `StreamId` | 論理ストリーム識別子。 |
| `LayoutId` | 論理ペイロード レイアウト識別子。 |
| `BlockSize` | コーデックのブロック サイズのメタデータ。 |
| `DecodeSampleSize` | デコーダーのサンプル サイズのメタデータ。 |
| `QuantizationMode` | コーデック オプションまたは従来の量子化バイト。 |
| `HeaderCrc32` | 計算中に CRC フィールドがゼロに設定されたヘッダー バイトの CRC32。 |

## 予約済みバイト

Header 0.2 は flag bit 0 (`FrameFlags.PayloadCrc32`) と `44..47` バイトを payload CRC32 に使い、`48..49` は予約領域としてゼロを維持します。`FrameHeader.PayloadCrc32` が checksum 値を保持します。Legacy 0.1 ヘッダーには flag がなく、`44..49` はゼロです。56バイト layout は維持されます。[Payload の整合性と互換性](../concepts/protocol.md#payload-integrity)を参照してください。

## ライタールール

ライターは常に CRC を書き込みます。

1. ヘッダーバイトを埋めます。
2. CRC フィールドにゼロを書き込みます。
3. CRC フィールドの前のヘッダー バイトに対して CRC を計算します。
4. CRC を最後の 4 バイトに書き込みます。

`CreateDefault()` は legacy 0.1 の既定値を維持します。`FrameFlags.PayloadCrc32` が有効なら `WriteTo` は指定された `PayloadCrc32` を格納し、minor version を最低0.2にして header CRC を完成します。payload 引数がないため、payload checksum 自体は計算しません。`FrameHeaderWriter.TryWritePayloadCrc32` は正確な payload バイトから計算し、flag・version・header CRC を更新します。実際の encoder は自動で呼び出します。

## リーダールール

リーダーは、不正なフレームをスローするのではなく、失敗したステータスを返す必要があります。ランタイム デコーダ コードは診断を設定し、ペイロード デコードを安全にスキップできます。

`FrameHeader.TryRead` は header を検証し、payload は検証しません。`FrameHeaderReader.TryValidatePayload` が存在する payload checksum を別に検査し、runtime decoder は本文の採用・適用より前に実行します。Header-only とフレーム順序 skip は本文を検査せず、予測 skip は CRC を通過した header メタデータだけを更新できます。Decoder で checksum を必須にしない限り legacy フレームは既定で受け入れられます。
