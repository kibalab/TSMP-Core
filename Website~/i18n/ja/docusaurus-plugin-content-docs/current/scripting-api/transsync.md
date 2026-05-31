---
title: トランスシンク
---

# トランスシンク

名前空間: `K13A.TSMP`

`TransSyncAttribute` は、TSMP が変数状態メッセージにエンコードするフィールドをマークします。

```csharp
[TransSync("example.value")]
public int syncedValue;
```

フィールドは `TSMPSetup` によって検出可能である必要があります。 `[TransSync]` フィールドを追加または削除した後、`Apply Setup` を実行します。

## プロパティ

| 財産 | タイプ | 意味 |
| --- | --- | --- |
| `Key` | `string` | フィールドのハッシュに使用される安定したキー。 |
| `Direction` | `NetworkSyncDirection` | 送信/受信方向。 |
| `Priority` | `int` | バインディング順序のヒント。 |
| `SendOnChange` | `bool` | 変更指向の送信ポリシーのメタデータ フラグ。 |
| `MinSendInterval` | `float` | 調整された送信ポリシーのメタデータ フラグ。 |
| `EnabledBy` | `string` | この同期フィールドを有効にするフィールドまたはプロパティの名前。 |

`transform.packed`、`animator.bytes`、`counter.value` などの明確で安定したキーを使用します。実行時に変更されるキーは使用しないでください。

## ネットワーク同期方向

| 価値 | 意味 |
| --- | --- |
| `SendReceive` | フィールドの送受信が可能です。 |
| `SendOnly` | フィールドはエンコードされていますが、受信時には適用されません。 |
| `ReceiveOnly` | フィールドは受信時に適用されますが、エンコードされません。 |

方向はセットアップ中に解決されます。変更後、セットアップを再実行してください。

## サポートされている値の型

- `bool`
- `int`
- `float`
- `Vector2`
- `Vector3`
- `Quaternion`
- `string`
- `byte[]`
- `bool[]`
- `int[]`
- `float[]`
- `Vector2[]`
- `Vector3[]`
- `Quaternion[]`
- `string[]`

高頻度データの場合は、パックされた `byte[]` フィールドを優先します。

## 有効化者

`EnabledBy` は、同じコンポーネントのフィールドまたはプロパティを指します。そのメンバーが false と評価された場合、バインディングはスキップされます。

フレームごとの複雑なロジックではなく、オプションのフィールドに使用します。フレームごとのパケット選択の場合は、選択肢を `byte[]` にパックし、パケット内にフラグを含めます。

## ペイロードに関するアドバイス

個別のフィールドにはそれぞれメッセージ オーバーヘッドがあります。いくつかのスカラー フィールドは問題ありませんが、繰り返される高頻度の値はパックする必要があります。

好む：

```csharp
[TransSync("pose.packed")]
public byte[] poseBytes;
```

多くの個々のボーン、ブレンド シェイプ、またはトランスフォーム フィールドにわたって。
