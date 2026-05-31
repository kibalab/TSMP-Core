---
title: TSMPNetworkBehaviour
---

# TSMPNetworkBehaviour

名前空間: `K13A.TSMP.Udon`

TSMP 同期動作の基本タイプ。

コンポーネントが TSMP メッセージ ルーティングに参加する必要がある場合にこれを使用します。ネットワーク ID、受信補間ポリシー、ライフサイクル フック、および `SendTransRPC` を提供します。

## フィールド

| 分野 | タイプ | 使用 |
| --- | --- | --- |
| `networkId` | `ushort` | ペイロード メッセージをこの動作にルーティングします。 |
| `receiveInterpolation` | `ReceiveInterpolationMode` | コントロールは動作を受け取ります。 |
| `continuousInterpolationRate` | `float` | 連続受信モードの平滑化率。 |
| `transRpcEncoder` | `TSMPEncoder` | `SendTransRPC` によって使用されるエンコーダー。セットアップによって割り当てられます。 |
| `lastVariableHash` | `uint` | 最後に受信した変数のハッシュ。 |
| `lastRpcHash` | `uint` | 最後に受信した RPC ハッシュ。 |
| `lastRpcNetworkId` | `ushort` | 最後に受信した RPC のネットワーク ID。 |
| `lastRpcArgumentCount` | `int` | 最後に受信した RPC の引数の数。 |
| `lastRpcMethodName` | `string` | 最後に受信した RPC のメソッド名。 |

通常のシーンでは `transRpcEncoder` を手動で割り当てないでください。 `TSMPSetup` に割り当ててください。

## 受信補間モード

| 価値 | 意味 |
| --- | --- |
| `None` | 受信した値を無視します。 |
| `Discrete` | 受け取った値を直接適用します。 |
| `Continuous` | ターゲットを保存し、サポートされている場合は補間します。 |

`Continuous` の使用方法は組み込みコンポーネントによって決まります。カスタム コンポーネントは、受信したデータを適用する前に `receiveInterpolation` をチェックする必要があります。

## ライフサイクルメソッド

| 方法 | いつ呼び出されるか |
| --- | --- |
| `TSMPBeforeEncode()` | エンコーダーが `[TransSync]` フィールドを読み取る前。 |
| `OnTSMPVariableReceived()` | デコーダが同期フィールドを適用した後。 |
| `OnTSMPVariableChanged(uint variableHash)` | 基本受信ハンドラーから。 |
| `OnTSMPRpcReceived()` | デコーダが TSMP RPC をディスパッチした後。 |
| `OnTSMPRpc(uint rpcHash)` | 基本 RPC 受信ハンドラーから。 |

典型的なカスタム コンポーネント フロー:

```csharp
public override void TSMPBeforeEncode()
{
    packedBytes = BuildPacket();
}

public override void OnTSMPVariableReceived()
{
    if (receiveInterpolation == ReceiveInterpolationMode.None)
        return;

    ApplyPacket(packedBytes);
    OnTSMPVariableChanged(lastVariableHash);
}
```

## ユーティリティメソッド

| 方法 | 使用 |
| --- | --- |
| `IsTSMPActive()` | 有効かつ階層内でアクティブな状態を返します。 |
| `GetReceiveInterpolationStep()` | クランプされた `0..1` 補間ステップを返します。 |
| `SendTransRPC(string methodName, RPCTarget target)` | TSMP を通じて RPC イベントをキューに入れます。 |

## RPCターゲット

| 価値 | 行動 |
| --- | --- |
| `Local` | ローカルのみで実行します。 |
| `Remote` | 受信者専用のキュー。 |
| `All` | ローカルで実行し、受信者を待ちます。 |

`SendTransRPC` はメソッド名をハッシュし、割り当てられたエンコーダーを通じてキューに入れます。受信側はデコード後メソッド名でディスパッチします。

## RPC の例

```csharp
public override void Interact()
{
    SendTransRPC(nameof(ToggleObject), RPCTarget.All);
}

public void ToggleObject()
{
    target.SetActive(!target.activeSelf);
}
```
