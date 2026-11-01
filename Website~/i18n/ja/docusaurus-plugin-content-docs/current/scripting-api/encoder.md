---
title: TSMPEncoder API
---

# `TSMPEncoder`

## Luma4 GPU エンコード

Luma4 はヘッダーと payload を RGBA バイトとしてアップロードし、GPU で小さなシンボルテクスチャを生成してブロック展開後に `output` へ出力します。パレット・ニブル順・CRC・パケット構造は変わりません。毎回全シンボルを描き直すため、payload 縮小時にも古いブロックは残りません。

マテリアルは Editor/ビルド準備で自動設定し、通常の Unity では Resources から取得します。手動の準備操作は不要です。非表示の `useGpuLuma4` は上級者向け無効化設定、`lastFrameUsedGpuLuma4` は最後に出力した経路を示します。リソース不足、native シェーダー非対応、対象外のレイアウトでは CPU 経路を維持します。出力サイズはブロックサイズの倍数、横38ブロック以上で、ヘッダー・payload・終端マーカーの領域が必要です。

Native コーデックは既定で false の `SupportsGpuLuma4Encoding` により明示的に参加します。Udon は標準 Luma4 シンボルモードだけに適用し、他の writer は変更しません。GPU リソースは encoder が所有し、無効化・破棄で解放します。最終画像には `output` を使ってください。非表示の `outputTexture` は CPU 作業用で、GPU 経路では更新されません。

測定した0・32・256バイトでも CPU 費用が減ったため、小さい payload も対象です。GPU パスを増やす CPU/GPU のトレードオフであり、全端末の GPU 時間が減るという意味ではありません。測定環境はデスクトップ D3D11 で、他の配布先端末では別途測定が必要です。

`TSMPEncoder` は、同期された動作、キューに入れられた RPC 呼び出し、および選択されたコーデックから TSMP フレームを構築します。結果を 1 つの出力 `RenderTexture` に書き込みます。

コードからエンコードを実行したり、フレーム カウンターを検査したり、TSMP RPC を送信したりする必要がある場合は、このページを使用します。

## コンポーネントフィールド

| 分野 | 目的 |
| --- | --- |
| `output` | エンコードされたフレームを受け取るテクスチャをレンダリングします。テクスチャ サイズは、最終的なフレーム サイズを定義します。 |
| `frameRate` | `autoEncode` が有効な場合の最大自動エンコード レート。 |
| `autoEncode` | コンポーネント更新ループでエンコードします。別のスクリプトが `EncodeNow()` を呼び出すときは無効にします。 |
| `clearAfterEncode` | Udon Writer: 出力成功後に作業中のフレームをリセットします（既定 `true`）。出力カウンターは残ります。通常の Unity では試行ごとに payload を再構築します。 |
| `selectedCodec` | ペイロード バイトをピクセルに変換するために使用されるコーデック コンポーネント。 Luma4 はデフォルトのコーデックです。 |
| `useBlockSymbolTexture` | 選択したコーデックがサポートしている場合は、コーデック ブロック テクスチャ パスを使用します。 |
| `networkBehaviours` | `[TransSync]` データおよび TSMP RPC メッセージに寄与する可能性のあるバインドされた動作。 |
| `transRpcRepeatFrames` | キュー内の TransRPC を含むフレームの出力に成功する総回数です。初回送信を含めて 1-16 回で、エンコードに失敗した場合は回数を消費しません。 |
| `transSyncRefreshInterval` | 変化のない自動 TransSync フィールドを再送する秒数（既定 1）。0 は再送を無効にし、フィールドごとの最小間隔は引き続き適用します。 |
| `streamId` | フレームヘッダーに書き込まれる論理ストリーム識別子。 |
| `layoutId` | フレームヘッダーに書き込まれる論理レイアウト識別子。 |

`TSMPSetup` は通常、バインディング フィールドとコーデック フィールドに入力します。手動編集は、カスタム ツールまたはテスト シーンにのみ役立ちます。

## 診断

| メンバー | 意味 |
| --- | --- |
| `EncodedObjectCount` / `encodedObjectCount` | 最後のフレームでエンコードされた可変ソースビヘイビアーの数。 |
| `QueuedRpcCount` / `queuedRpcCount` | エンコードを待機している RPC メッセージの数。 |
| `PayloadBytes` / `payloadBytes` | 最後のペイロードに書き込まれたバイト数。 |
| `usablePayloadBytes` | フレームヘッダーとコーデックレイアウト後のペイロード容量が考慮されます。 |
| `deferredVariableCount` | 直近の試行で容量不足により見送った送信対象フィールド数です。後の試行で再送します。 |
| `messageCount` | 最後のネットワーク フレーム内の可変メッセージと RPC メッセージ。 |
| `variableMessageCount` | 変数状態メッセージの数。 |
| `rpcMessageCount` | RPC メッセージの数。 |
| `FrameIndex` / `frameIndex` | ヘッダーに書き込まれる単調フレームカウンター。 |
| `lastEncodeStage` | Udon の整数段階: 0 初期、1 準備、2 自動フィールド、3 パケット/ヘッダー、4 テクスチャ書き込み、5 出力/反映、6 完了。エラー時は前の段階で停止することがあります。 |
| `LastError` / `lastError` | 最後のエンコーダエラー。ランタイム ビルドでは重要なエラーもログに記録されます。 |

Udon の出力成功後は、自動リセットしても `payloadBytes`、`messageCount`、`variableMessageCount`、`rpcMessageCount` を Inspector とデバッグ画面から読み取れます。直近の試行の値であり、最後の成功を永続保存する値ではありません。`BeginFrame()`、明示的な `ClearFrame()`、空の試行、失敗した試行ではリセットされます。出力成功は `frameIndex`、失敗理由は `lastError` で確認してください。Writer のリセットは出力ピクセルやパケットバッファの内容を消去しません。

## `EncodeNow()`

```csharp
public void EncodeNow()
```

1 フレームを即座にエンコードします。 `autoEncode` を無効にして呼び出しても安全です。

一般的な用途:

- フレームを強制するデバッグ ボタン。
- 独自のタイミングを持つキャプチャ パイプライン。
- 編集時プレビュー ツール。

`EncodeNow()` 可変データもキューイングされた RPC データもない場合はフレームを書き込まずにリターンします。

フィールドが変化していない場合や最小間隔の待機中は、新しいフレームを生成しないことがあります。エラーではなく、フレーム番号も増えません。`ResetFrameIndex()` は送信スナップショットも初期化し、次の試行で初期状態を再送します。

## `ResetFrameIndex()`

```csharp
public void ResetFrameIndex()
```

ヘッダーと診断で使用されるフレーム カウンターをリセットします。これは、クリーンなキャプチャまたは再現可能なテストに役立ちます。

## `QueueRpc()` と `QueueRpcHash()`

```csharp
public bool QueueRpc(ushort networkId, string rpcName, params object[] arguments)
public bool QueueRpcHash(ushort networkId, uint rpcHash, params object[] arguments)
```

下位レベルの RPC メッセージをキューに入れます。ほとんどのユーザー コンポーネントは動作ネットワーク ID をすでに知っているため、`TSMPNetworkBehaviour.SendTransRPC()` を優先する必要があります。

引数にはプリミティブ TSMP 値型を使用する必要があります。高頻度のデータや複雑なデータの場合は、多くの RPC 引数を送信する代わりに、データを `[TransSync] byte[]` フィールドにパックします。

この下位 API は UdonSharp のない通常の Unity で使用できます。戻り値の `true` はキューへの登録成功であり、配達成功ではありません。非対応型や null の引数、255 個を超える引数、プロトコル上限または設定済みフレーム容量を超えるメッセージは `false` を返します。理由は `lastError` で確認できます。

登録後の引数変更やコーデック・出力容量の縮小によって送信不能になった RPC は、そのイベントだけを破棄し、診断を記録して後続 RPC と変数の送信を続けます。警告ログは Encoder のデバッグ設定に従います。出力やコーデックの未設定、フレーム自体を構成できない容量、コーデックの書き込み失敗では、有効なイベントの送信回数を消費しません。

## `QueueTransRpc()`

```csharp
public bool QueueTransRpc(int networkId, uint rpcHash, string methodName)
```

ネットワーク ID とメソッド ハッシュによって TSMP RPC をキューに入れます。これは、`TSMPNetworkBehaviour.SendTransRPC()` によって使用されるエンコーダ側のエントリ ポイントです。

キューは `transRpcRepeatFrames` に従って後続のフレームに書き込まれるため、単一のイベントはテクスチャ パスでの短いフレーム ドロップに耐えることができます。

残り回数を減らすのは、そのイベントを含むフレームのローカル出力に成功した場合だけです。`TSMPBeforeEncode()` 中に登録した RPC は、回数を減らさず次のフレームまで保持します。設定回数を使い切れば、受信側に一度も届かなかった場合でも送信を終了します。通常の Unity では、`QueueTransRpc()` にも上記の登録時検証と送信不能イベントの処理を適用します。

これは繰り返し送信であり、受信確認による配信保証ではありません。イベントを含むすべてのフレームが失われると、RPC も失われます。Decoder は Stream ID、Network ID、メソッドハッシュ、イベント ID を使って直近 32 件の異なるイベントを記憶し、重複実行を防ぎます。1 つの Decoder で複数の送信元を受信する場合は、それぞれ異なる `streamId` を使用してください。送信元を再起動して同じ Stream ID とイベント ID を再利用すると、以前のキャッシュと衝突する可能性があります。

## 生の変数ライター API

これらの Udon Writer メンバーは、高度なツールと生成されたコードを対象としています。

```csharp
public void BeginFrame()
public void ClearFrame()
public bool BeginVariableState(int networkId)
public bool WriteRawBytesVariable(uint variableHash, byte[] value)
public bool EndVariableState()
public bool BeginRpcCall(int networkId, uint rpcHash)
public bool WriteStringRpcArgument(string value)
public bool WriteInt32RpcArgument(int value)
public bool EndRpcCall()
public void CancelCurrentMessage()
```

カスタム変数メッセージにはこのフローを使用します。

```csharp
encoder.BeginFrame();
if (encoder.BeginVariableState(networkId) &&
    encoder.WriteRawBytesVariable(variableHash, payload))
{
    encoder.EndVariableState();
}
else
{
    encoder.CancelCurrentMessage();
}
```

通常のコンポーネントの場合、`[TransSync]` の方が単純で、エラーが発生しにくくなります。

## ランタイムルール

- エンコーダは、オプションのコーデック パッケージの種類を認識すべきではありません。選択した `TSMPCodec` と通信します。
- 出力テクスチャは、選択したコーデックとペイロード サイズに対して十分な大きさである必要があります。
- ペイロード オーバーフローが発生すると、警告がログに記録され、予算超過メッセージが表示されます。
- RPC 専用フレームは有効です。フレームには可変メッセージを含めることはできませんが、キューに入れられた RPC メッセージを伝送することはできます。
