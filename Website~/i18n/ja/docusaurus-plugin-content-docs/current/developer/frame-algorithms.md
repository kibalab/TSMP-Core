---
title: フレームとペイロードのアルゴリズム
---

# フレームとペイロードのアルゴリズム

このページでは、実行時のデータ フローについて概要を説明します。これは、エンコーダ/デコーダの変更を確認したり、テストを追加したり、フレームに含まれるメッセージが予想よりも少ない理由をデバッグしたりするときに役立ちます。

## エンコーダのフレームフロー

```text
EncodeNow
  -> collect active TSMPNetworkBehaviour targets
  -> begin network payload
  -> for each behaviour:
       call TSMPBeforeEncode
       write enabled TransSync fields
       cancel empty VariableState messages
  -> write queued RPC messages, if any
  -> patch message count
  -> write frame header with CRC
  -> ask codec to draw header and payload into output texture
```

重要な行動:

- 空の `VariableState` メッセージはロールバックされます。
- RPC のみのフレームが許可されます。
- キューに入れられた RPC は複数のフレームにわたって繰り返すことができます。
- ペイロード コピーと FEC は現在のワイヤ形式の一部ではありません。
- 選択されたコーデックは、ペイロード開始行、容量、シンボル モード、およびオプション バイトを提供します。

## ネットワークペイロードのレイアウト

```text
+-----------------------+-----------------------------+
| Network frame header  | Message 0 | Message 1 | ... |
| 8 bytes               | variable state or RPC       |
+-----------------------+-----------------------------+
```

ネットワークフレームヘッダー:

| オフセット | サイズ | 分野 |
| --- | ---: | --- |
| `0` | 1 | ネットワークのメジャー バージョン。 |
| `1` | 1 | ネットワークのマイナーバージョン。 |
| `2` | 2 | メッセージ数。 |
| `4` | 4 | 順序。 |

メッセージヘッダー:

| オフセット | サイズ | 分野 |
| --- | ---: | --- |
| `0` | 2 | ネットワークID。 |
| `2` | 1 | メッセージの種類。 |
| `3` | 1 | フラグ。 |
| `4` | 2 | メッセージシーケンス。 |
| `6` | 2 | 体長。 |

## 変数状態メッセージ

```text
+----------------+------------------+------------------+
| Variable count | Variable value 0 | Variable value N |
| 2 bytes        | 7-byte header + data                 |
+----------------+------------------+------------------+
```

変数値ヘッダー:

| オフセット | サイズ | 分野 |
| --- | ---: | --- |
| `0` | 4 | 変数ハッシュ。 |
| `4` | 1 | 値のタイプ。 |
| `5` | 2 | 値のバイト長。 |

デコーダは、変数ハッシュとネットワーク ID を、生成されたバインディング エントリと照合します。

エンコーダー ルール: 動作に対して変数が書き込まれなかった場合、メッセージはキャンセルされ、メッセージ数は増加しません。

## RPC メッセージ

```text
+----------+----------------+----------------+----------------+
| RPC hash | Argument count | Argument 0     | Argument N     |
| 4 bytes  | 1 byte         | 3-byte header + data             |
+----------+----------------+----------------+----------------+
```

RPC 引数ヘッダー:

| オフセット | サイズ | 分野 |
| --- | ---: | --- |
| `0` | 1 | 値のタイプ。 |
| `1` | 2 | 値のバイト長。 |

デコーダはペイロード データを通じてメソッド名を解決し、`TSMPBehaviour.SendCustomEvent` を通じてイベントを送出します。

RPC イベントは、テクスチャ フレームの損失を許容するために、エンコーダによって少数のフレームに対して繰り返されます。

## ヘッダーの検証

デコーダの検証順序:

1. バッファ範囲。
2. 魔法。
3. ヘッダーのサイズ。
4. メジャーバージョン。
5. ヘッダー CRC32。

CRC はヘッダー バイト `0..51` に対して計算されます。保存された CRC はバイト `52..55` にあります。不一致がある場合、ペイロード処理の前にフレームが破棄されます。

## デコードの流れ

```text
Update / decode tick
  -> read header area from texture
  -> validate header and CRC
  -> choose codec by codecId
  -> request payload bytes according to PayloadSize
  -> decode network frame header
  -> iterate messages
  -> apply variable values or dispatch RPC calls
  -> update diagnostics
```

デコーダはフェールクローズされるはずです。不正なペイロード データはペイロード処理を停止し、部分的に無効な状態を適用する代わりに診断を記録します。

## テストを追加する場所

以下のプロトコル テストを追加します。

- ヘッダー CRC の生成と拒否。
- メッセージ数のパッチ適用。
- 空の変数メッセージのロールバック。
- RPC 専用フレーム。
- ペイロード容量の境界。
- 重複フレームおよび重複 RPC 処理。
