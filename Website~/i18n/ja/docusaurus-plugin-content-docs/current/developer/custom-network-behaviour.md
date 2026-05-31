---
title: カスタムネットワーク動作
---

# カスタムネットワーク動作

組み込みの同期コンポーネントでは必要なデータをカバーできない場合は、カスタム ネットワーク コンポーネントを作成します。

カスタム コンポーネントは `K13A.TSMP.Udon.TSMPNetworkBehaviour` から継承する必要があります。これにより、コンポーネントにネットワーク ID、受信補間設定、TSMP RPC ヘルパー、およびエンコード/受信ライフサイクル フックが与えられます。

## 基本形状

```csharp
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public class ExampleCounterSync : TSMPNetworkBehaviour
{
    public int localCounter;

    [TransSync("counter.value")]
    public int syncedCounter;

    public override void TSMPBeforeEncode()
    {
        syncedCounter = localCounter;
    }

    public override void OnTSMPVariableReceived()
    {
        base.OnTSMPVariableReceived();

        if (receiveInterpolation == ReceiveInterpolationMode.None)
            return;

        localCounter = syncedCounter;
    }
}
```

`[TransSync]` キーは安定している必要があります。これを変更すると変数ハッシュが変更されるため、既存の送信者/受信者のバインディングは一致しなくなります。

## エンコーディングのライフサイクル

エンコーダは `TSMPBeforeEncode()` フィールドを読み取る前に `[TransSync]` を呼び出します。

これを使用して次のことを行います。

- シーンの状態を同期フィールドにコピーします。
- 複数の値を 1 つの `byte[]` にパックします。
- `IsTSMPActive()` が false の場合は作業を回避します。
- 高価な計算をデコーダ パスの外側に置きます。

`Apply Setup` を呼び出したり、`TSMPBeforeEncode()` 内で大きな一時配列を割り当てたりしないでください。すべてのエンコードされたフレームを実行できます。

## 受信ライフサイクル

デコーダは受信したフィールド値を設定し、`lastVariableHash` を設定して、`OnTSMPVariableReceived()` を呼び出します。

次の必要がある場合は `OnTSMPVariableReceived()` をオーバーライドします。

- パックされたバイト配列を適用します。
- ターゲット補間値を更新します。
- `receiveInterpolation == None`の場合は値を無視します。
- 変更された変数のみを適用します。

基本的な `base.OnTSMPVariableReceived()` 処理が必要な場合は、`lastVariableHash` を呼び出します。

## パックされたバイト配列

高頻度データの場合は、状態を `byte[]` にパックします。

良い候補者:

- 変身します。
- 骨の回転。
- シェイプのウェイトをブレンドします。
- 複数の小さな数値フィールド。
- プレイヤーごとのポーズ記録。

パックされた配列により、ペイロード サイズが予測可能に保たれ、Udon ブリッジの呼び出しが削減されます。通常、単一の `byte[]` フィールドは、多数のスカラー フィールドよりも安価です。

## RPC イベント

イベントのようなアクションには `SendTransRPC` を使用します。



`RPCTarget.All` はメソッドをローカルで即座に実行し、TSMP ストリームを通じて受信者のキューに入れます。ループバック テストでは、イベントが今一度発生し、トランスポート遅延後にもう一度発生することを意味します。

連続状態ではなく、イベントに対して RPC を使用します。連続状態は `[TransSync]` フィールドに属します。

## セットアップ要件

カスタム動作を追加した後:

1. シーンまたはプレハブに追加します。
2. `Apply Setup` を実行します。
3. ネットワーク ID を受信して​​いることを確認します。
4. `[TransSync]` フィールドが生成されたバインディングに表示されることを確認します。
5. `TSMPDebugCanvas` を表示してテストします。

コンポーネントがデータを送信しない場合は、アクティブ状態、`[TransSync]` の方向、`TSMPBeforeEncode()` が空ではないフィールドを生成しているかどうかを確認してください。

## UdonSharp 互換性チェックリスト

- UdonSharp の動作に対する一般的なメソッドは避けてください。
- 実行時パスでの LINQ とリフレクションを回避します。
- 明示的な `for` ループを使用します。
- パックされたデータには `byte[]` を優先します。
- 公開同期フィールドはシンプルにしてください。
- フィールドタイプを変更した後、UdonSharp コンパイルをテストします。
