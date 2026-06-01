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

| プロパティ | 型 | 既定値 | できること |
| --- | --- | --- | --- |
| `Key` | `string` | `null` | 変数ハッシュの計算に使う安定した識別子です。省略した場合はフィールド名が使われます。送信側と受信側を一致させるには、同じ key、network ID、value type を使う必要があります。 |
| `Direction` | `NetworkSyncDirection` | `SendReceive` | `Apply Setup` がこのフィールドを encoder binding table、decoder binding table、またはその両方に入れるかを決めます。入力用フィールドと表示用フィールドを分けるときに使います。 |
| `Priority` | `int` | `0` | 生成された receiver binding table に保存される ordering hint です。現在の runtime は、この値で variable write の順序変更や throttle を行いません。 |
| `SendOnChange` | `bool` | `true` | 変更ベース送信のための metadata です。Attribute としては受け付けられますが、現在の encoder は encode 時に bound field を sample します。Runtime の bandwidth limiter としては期待しないでください。 |
| `MinSendInterval` | `float` | `0` | レート制限送信のための metadata です。Attribute としては受け付けられますが、現在の encoder は field ごとの interval を強制しません。独自の pacing が必要な場合は component logic または packed `byte[]` flag を使います。 |
| `EnabledBy` | `string` | `null` | 同じ component にある `bool` field または property の名前です。`Apply Setup` が binding を作るとき、その member が false の場合、この field は generated binding table から除外されます。 |

`transform.packed`、`animator.bytes`、`counter.value` などの明確で安定したキーを使用します。実行時に変更されるキーは使用しないでください。

### `Key`

`Key` は wire 上の field identity です。異なるフィールド名でも、同じ variable として同期できます。

```csharp
public class ChatInput : TSMPNetworkBehaviour
{
    [TransSync("chat.text", Direction = NetworkSyncDirection.SendOnly)]
    public string outgoingText;
}

public class ChatOutput : TSMPNetworkBehaviour
{
    [TransSync("chat.text", Direction = NetworkSyncDirection.ReceiveOnly)]
    public string incomingText;
}
```

両方の field が `chat.text` を使うため、TSMP は同じ variable hash を割り当てます。World を公開した後は key を安定させてください。Key を変更または rename した場合は、`Apply Setup` を再実行し、sender と receiver の両方が更新されていることを確認します。

### `Direction`

`Direction` は、generated binding table で field がどちら側に使われるかを決めます。

| 値 | 主な用途 |
| --- | --- |
| `SendReceive` | 同じ field を encode し、decoded value も受け取れる単純な mirrored state。自己 loopback の同一 local field でなければ最も簡単です。 |
| `SendOnly` | Source/input field。Encoder は読めますが、decoder はこの field に書き戻しません。Local UI input、local tracking state、self-loop test に使います。 |
| `ReceiveOnly` | Display/output field。Decoder は書き込めますが、encoder は読みません。Text label、proxy avatar、remote-only state、delayed loopback display に使います。 |

`instance A encoder -> stream -> instance A decoder` のような loopback test では、入力 field を `SendOnly`、別の出力 field を `ReceiveOnly` にするのが安全です。1 つの `SendReceive` field を両方に使うと、遅延した過去 frame が現在の local value を上書きすることがあります。

### `Priority`, `SendOnChange`, `MinSendInterval`

これらは送信ポリシーの意図を表す metadata に近く、現在の runtime では field ごとの scheduler として動作しません。

- `Priority` は receiver binding に保存され、tooling または将来の scheduler で利用できます。
- `SendOnChange` は、現在の encoder が field を sample することを止めません。
- `MinSendInterval` は、現在その field の送信間隔を throttle しません。

現在の実装で bandwidth を制御したい場合は、`TSMPBeforeEncode()` で自分で値を作り、変更がないときは同じ値、空 packet、または flag を含む packed `byte[]` を書きます。高頻度データでは、多数の scalar field より 1 つの packed field の方が安く、制御もしやすいです。

### `EnabledBy`

`EnabledBy` は setup-time optional field に向いています。

```csharp
public bool includeVelocity = true;

[TransSync("velocity", EnabledBy = nameof(includeVelocity))]
public Vector3 velocity;
```

`Apply Setup` の実行時に、TSMP は `includeVelocity` を確認します。false の場合、この field は generated binding table に入りません。アップロード済みの Udon world では、これは高頻度 runtime toggle ではなく binding-generation option として扱ってください。毎 frame 送るデータを変えたい場合は、binding は残し、packed `byte[]` の中に flag を入れる方式が安全です。

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

## バインディング再生成

`Key`、`Direction`、value type、`EnabledBy` は generated binding に影響します。これらを変更した後は、`TSMPSetup` で `Apply Setup` を実行してください。アップロード済みの VRChat world では、TSMP は runtime reflection ではなく generated table を使います。

## ペイロードに関するアドバイス

個別のフィールドにはそれぞれメッセージ オーバーヘッドがあります。いくつかのスカラー フィールドは問題ありませんが、繰り返される高頻度の値はパックする必要があります。

好む：

```csharp
[TransSync("pose.packed")]
public byte[] poseBytes;
```

多くの個々のボーン、ブレンド シェイプ、またはトランスフォーム フィールドにわたって。
