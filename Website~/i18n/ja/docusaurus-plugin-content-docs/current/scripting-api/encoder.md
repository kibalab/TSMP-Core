---
title: TSMPEncoder API
---

# `TSMPEncoder`

`TSMPEncoder` は、同期された動作、キューに入れられた RPC 呼び出し、および選択されたコーデックから TSMP フレームを構築します。結果を 1 つの出力 `RenderTexture` に書き込みます。

コードからエンコードを実行したり、フレーム カウンターを検査したり、TSMP RPC を送信したりする必要がある場合は、このページを使用します。

## コンポーネントフィールド

| 分野 | 目的 |
| --- | --- |
| `output` | エンコードされたフレームを受け取るテクスチャをレンダリングします。テクスチャ サイズは、最終的なフレーム サイズを定義します。 |
| `frameRate` | `autoEncode` が有効な場合の最大自動エンコード レート。 |
| `autoEncode` | コンポーネント更新ループでエンコードします。別のスクリプトが `EncodeNow()` を呼び出すときは無効にします。 |
| `clearAfterEncode` | 現在のフレームを書き込んだ後、未使用のフレーム ピクセルをクリアします。コーデックやペイロード サイズを頻繁に変更する場合は、これを有効にします。 |
| `selectedCodec` | ペイロード バイトをピクセルに変換するために使用されるコーデック コンポーネント。 Luma4 はデフォルトのコーデックです。 |
| `useBlockSymbolTexture` | 選択したコーデックがサポートしている場合は、コーデック ブロック テクスチャ パスを使用します。 |
| `networkBehaviours` | `[TransSync]` データおよび TSMP RPC メッセージに寄与する可能性のあるバインドされた動作。 |
| `transRpcRepeatFrames` | キューに入れられた RPC を表示し続ける追加フレームの数。損失の多いキャプチャ パスの場合は、これを増やします。 |
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
| `messageCount` | 最後のネットワーク フレーム内の可変メッセージと RPC メッセージ。 |
| `variableMessageCount` | 変数状態メッセージの数。 |
| `rpcMessageCount` | RPC メッセージの数。 |
| `FrameIndex` / `frameIndex` | ヘッダーに書き込まれる単調フレームカウンター。 |
| `lastEncodeStage` | 最後のエンコード段階の短いテキスト識別子。 |
| `LastError` / `lastError` | 最後のエンコーダエラー。ランタイム ビルドでは重要なエラーもログに記録されます。 |

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

## `ResetFrameIndex()`

```csharp
public void ResetFrameIndex()
```

ヘッダーと診断で使用されるフレーム カウンターをリセットします。これは、クリーンなキャプチャまたは再現可能なテストに役立ちます。

## `QueueRpc()` と `QueueRpcHash()`

```csharp
public void QueueRpc(ushort networkId, string rpcName, params object[] arguments)
public void QueueRpcHash(ushort networkId, uint rpcHash, params object[] arguments)
```

下位レベルの RPC メッセージをキューに入れます。ほとんどのユーザー コンポーネントは動作ネットワーク ID をすでに知っているため、`TSMPNetworkBehaviour.SendTransRPC()` を優先する必要があります。

引数にはプリミティブ TSMP 値型を使用する必要があります。高頻度のデータや複雑なデータの場合は、多くの RPC 引数を送信する代わりに、データを `[TransSync] byte[]` フィールドにパックします。

## `QueueTransRpc()`

```csharp
public void QueueTransRpc(int networkId, uint rpcHash, string methodName)
```

ネットワーク ID とメソッド ハッシュによって TSMP RPC をキューに入れます。これは、`TSMPNetworkBehaviour.SendTransRPC()` によって使用されるエンコーダ側のエントリ ポイントです。

キューは `transRpcRepeatFrames` に従って後続のフレームに書き込まれるため、単一のイベントはテクスチャ パスでの短いフレーム ドロップに耐えることができます。

## 生の変数ライター API

これらのメンバーは、高度なツールと生成されたコードを対象としています。

```csharp
public bool BeginFrame()
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
if (encoder.BeginFrame() &&
    encoder.BeginVariableState(networkId) &&
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
