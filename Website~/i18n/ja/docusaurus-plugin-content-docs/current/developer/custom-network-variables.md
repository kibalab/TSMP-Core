---
title: カスタムネットワーク変数
---

# カスタムネットワーク変数

`[TransSync]` がフレームごと、または値が変更されるたびにデータを送信する必要がある場合は、`TSMPNetworkBehaviour` を使用します。エンコーダーはフィールドを読み取り、変数メッセージを書き込み、デコーダーはそれを一致する受信フィールドに適用します。

## 最小コンポーネント

```csharp
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public class DoorStateSync : TSMPNetworkBehaviour
{
    [TransSync("door.open")]
    public bool isOpen;

    public override void TSMPBeforeEncode()
    {
        isOpen = transform.localEulerAngles.y > 45f;
    }

    public override void OnTSMPVariableReceived()
    {
        transform.localRotation = Quaternion.Euler(0f, isOpen ? 90f : 0f, 0f);
    }
}
```

## `[TransSync]` キー

```csharp
[TransSync("health.current")]
public int health;
```

キーは安定した `variableHash` にハッシュされます。フィールドの名前が後で変更される可能性がある場合は、フィールド名に依存する代わりに明示的なキーを使用します。

## サポートされている値の型

| タイプグループ | 例 |
| --- | --- |
| ブール値と数値 | `bool`、`int`、`float` |
| Unity 構造体 | `Vector2`、`Vector3`、`Quaternion` |
| 文章 | `string` |
| パックされたデータ | `byte[]` |
| 配列 | サポートされているプリミティブ配列と構造体配列 |

`byte[]` を使用すると、バイト レイアウトを制御し、多数の小さなフィールド メッセージを回避できるため、高頻度の値には `byte[]` を推奨します。

＃＃ 方向



| 方向 | 意味 |
| --- | --- |
| `SendReceive` | 送信者がそれを書き込み、受信者がそれを適用します。 |
| `SendOnly` | 送信者はこれを書き込みますが、このフィールドは受信者のターゲットとして使用されません。 |
| `ReceiveOnly` | 受信者はそれを適用しますが、このフィールドは送信者によって書き込まれません。 |

## 条件付き有効化



`EnabledBy` は、同じコンポーネント上のブール型フィールドを指します。 false の場合、変数はスキップされます。変数メッセージにフィールドがない場合、そのメッセージはペイロードから削除されます。

## 受信補間

各 `TSMPNetworkBehaviour` には `receiveInterpolation` があります。

| モード | 行動 |
| --- | --- |
| `None` | 受信した値を無視します。 |
| `Discrete` | フレームがデコードされるときに値を適用します。 |
| `Continuous` | コンポーネントが継続的なアプリケーションを実装する場合、受信した値に向かって滑らかになります。 |

カスタム コンポーネントは `GetReceiveInterpolationStep()` を使用して独自のスムージングを実行できます。

## セットアップ要件

`[TransSync]` フィールドを追加、削除、または名前変更した後、`Apply Setup` を実行します。 TSMP は、アップロードされたワールドでのランタイム リフレクションの代わりに、生成されたバインディング テーブルを使用します。
