---
title: TSMP.cginc
---

シェーダーターゲット 3.5 以上の**データを利用する側のシェーダー**で使うインクルードです。デコーダーが検証・公開したデータを読みます。エンコード済みの映像テクスチャを直接デコードしたり、コーデックを実装したりするものではありません。動作例は[マテリアルで TSMP のデータを読む](../guides/shader-receive.md)を参照してください。

```hlsl
#pragma target 3.5
#include "Packages/com.kibalab.tsmp.core/Shaders/TSMP.cginc"
```

## 取得関数と読み出し関数

数値引数はすべて符号なし 32 ビット整数です。取得関数は `TSMPByteView` を設定し、指定したデータがなければ `false` を返します。`TSMPTryGetFrame(out view)` と `TSMPTryGetField(networkId, hash, out view)` はチャンネル 0 用の短縮形です。

| HLSL | 結果 |
| --- | --- |
| `bool TSMPTryGetFrame(channel, out view)` | 56 バイトの通信ヘッダーを除く最新の [NetworkFrame ペイロード](./network-frame.md)。RPC エントリーもバイトとして読めますが、イベントキューではありません。 |
| `bool TSMPTryGetField(channel, networkId, hash, out view)` | エントリーヘッダーを含まない、指定したキーの最新値。受信済みの空の値でも true を返します。 |
| `uint TSMPStreamId(channel)` | 公開フレームのストリーム ID。先にフレームの有無を確認してください。 |
| `uint TSMPFrameIndex(channel)` | フレーム番号。正常なフレームでも番号は 0 になり得ます。 |
| `uint TSMPReadByte(view, offset)` | 1 バイトの整数。 |
| `uint TSMPReadUInt16LE(view, offset)` | リトルエンディアンの 2 バイト整数。 |
| `uint TSMPReadUInt32LE(view, offset)` | リトルエンディアンの 4 バイト整数。 |
| `float TSMPReadFloat32LE(view, offset)` | 4 バイトのビット列を IEEE 754 Float32 として解釈した値。整数を浮動小数に変換する関数ではありません。 |
| `uint TSMPReadBytesLE(view, offset, count)` | 1～4 バイトをリトルエンディアンで読み出します。それ以外の個数では 0 を返します。 |

ビューの `length` はバイト数です。`valueType` はフィールドの通信型 `NetworkValueType` で、フレーム全体のビューでは 0 です。オフセットには**ビューの先頭からのバイト数**を渡します。非整列アドレスにも対応し、要求した範囲が 1 バイトでもはみ出すと 0 を返します。値が本当に 0 なのかを区別するには、取得結果と長さを先に確認します。`view.channel` と `view.offset` は内部アドレスです。同じシェーダー呼び出し内ではビューを再利用できますが、次のフレームでは取得し直してください。

## フィールドの選択 {#field-key}

キーは送信側のネットワーク ID と通信で使う 32 ビットハッシュの組です。バインディングがある場合は **Copy Shader Lookup** から HLSL 式をコピーできます。受信側にバインディングがない場合は C# の `StableHash.VariableHash(behaviour.GetType(), fieldName, transSyncKey)` で算出してください。名前空間を含む型名、ドット、TransSync キー（空ならフィールド名）を連結した文字列のハッシュです。`Shader.PropertyToID` とは異なります。float に格納すると下位ビットが失われることがあるため、`0xFEDCBA98u` のような `uint` で渡してください。

## 値の保持とコスト

SendOnChange で省略されたフィールドは最後の値を保持します。同一フレーム内でキーが重複した場合は最後のエントリーを使用します。ストリームの変更時やフレーム番号の再開始が受理されたときは、以前のフィールドを破棄します。フィルターで除外されたフレーム、不正なフレーム、診断用セーフモードのフレームは公開しません。デコーダーを無効にしたりチャンネルを変更したりすると以前のデータを解放します。

既定の **Off** では公開しません。有効時は、公開するフレームごとにフィールドの位置表をアップロードし、デコード済みバイトを読む GPU パスを 1 回実行します。GPU readback の追加はなく、CPU での検証・バインディング・RPC は継続します。チャンネルあたり最大 1,024 フィールド、値の合計 1 MiB（各値を 4 バイト単位に切り上げ）を保持できます。いずれかの上限を超えるとチャンネルを無効化して警告を出します。CPU デコードは継続し、次に公開できるフレームからキャッシュを作り直します。
