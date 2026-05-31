---
title: カスタムネットワークRPC
---

# カスタムネットワーク RPC

イベントがテクスチャ ストリームを通過する必要がある場合は、TSMP RPC を使用します。これは、遅延インタラクション、トグル、トリガー、およびワンショットの状態遷移に役立ちます。

TSMP RPC はメソッド呼び出しを自動的に挿入しません。 `SendTransRPC(methodName, target)` から `TSMPNetworkBehaviour` に電話します。

## 最小限の RPC コンポーネント

```csharp
using K13A.TSMP.Udon;
using UnityEngine;

public class BellSync : TSMPNetworkBehaviour
{
    public AudioSource bell;

    public override void Interact()
    {
        SendTransRPC(nameof(Ring), RPCTarget.All);
    }

    public void Ring()
    {
        if (bell != null)
        {
            bell.Play();
        }
    }
}
```

## `SendTransRPC()`



| パラメータ | 意味 |
| --- | --- |
| `methodName` | ターゲットの動作に対して実行するパブリック メソッド名。可能な場合は `nameof(Method)` を使用してください。 |
| `target` | `Local`、`Remote`、または `All`。 |

## `RPCTarget`

| ターゲット | 送信者の動作 | 受信機の動作 |
| --- | --- | --- |
| `Local` | メソッドをすぐに実行します。 | 何もキューに入れられていません。 |
| `Remote` | ローカルでは実行されません。 | デコードされた受信者のキューメソッド。 |
| `All` | すぐに実行されます。 | デコードされた受信者のキューメソッドも使用します。 |

送信者と受信者が OBS または別のキャプチャ パスを介してループバックされる場合、`All` はすぐに 1 回実行され、キャプチャ遅延後に再度実行される可能性があります。

## 信頼性とリピートフレーム

RPC はエンコーダ上の `transRpcRepeatFrames` に表示されます。次の場合にこれを増やします。

- キャプチャ パスでフレームがドロップされます。
- OBS または Spout は不規則なフレーム ペーシングを生成します。
- インタラクションが失われています。

急速に繰り返されるイベントには、非常に高い繰り返し回数を使用しないでください。受信者は同じイベントを予想よりも長く見る可能性があります。

## メソッドの制限

- メソッドは受信側 `TSMPNetworkBehaviour` に存在する必要があります。
- Udon セーフ パスのために、RPC メソッドをパブリックにして引数なしで維持します。
- イベントにパラメータが必要な場合は、`[TransSync]` フィールドにデータを入力します。
- フレームの繰り返しが可能な場合、副作用を冪等に保ちます。

## デバッグチェックリスト

| 症状 | チェック |
| --- | --- |
| ローカルアクションは機能するが、遅延アクションは機能しない | エンコーダ `queuedRpcCount`、`rpcMessageCount`、およびデコーダ `lastRpcCallCount`。 |
| イベントが欠落しました | `transRpcRepeatFrames` を増やし、デバッグ キャンバスでフレーム損失を確認します。 |
| 間違ったオブジェクトがイベントを受信しました | `Apply Setup` を実行し、`networkId` 値が一意であることを確認します。 |
