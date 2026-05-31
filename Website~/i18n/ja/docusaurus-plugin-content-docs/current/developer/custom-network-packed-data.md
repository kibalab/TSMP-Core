---
title: カスタムパックされたデータ
---

# カスタムパックされたデータ

帯域幅を正確に制御する必要がある場合は、`[TransSync] byte[]` フィールドを使用します。これは、ポーズ データ、密変換データ、ブレンドシェイプ選択、およびその他の高周波値に推奨されるパターンです。

## パケットレイアウト

すべてのパケットはバージョン バイトで始まります。

| オフセット | 価値 |
| --- | --- |
| `0` | パケット版。 |
| `1..n` | そのバージョンのデータ。 |

これにより、受信側はバイトを誤って読み取る代わりに、未知のバージョンを無視できるようになります。

## パケットの例

```csharp
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public class PackedPointSync : TSMPNetworkBehaviour
{
    [TransSync("point.packed")]
    public byte[] pointBytes = new byte[13];

    public Transform target;

    public override void TSMPBeforeEncode()
    {
        if (target == null || pointBytes == null || pointBytes.Length < 13)
        {
            return;
        }

        Vector3 p = target.localPosition;
        pointBytes[0] = 1;
        Binary.WriteFloat32(pointBytes, 1, p.x);
        Binary.WriteFloat32(pointBytes, 5, p.y);
        Binary.WriteFloat32(pointBytes, 9, p.z);
    }

    public override void OnTSMPVariableReceived()
    {
        if (target == null || pointBytes == null || pointBytes.Length < 13 || pointBytes[0] != 1)
        {
            return;
        }

        float x = Binary.ReadFloat32(pointBytes, 1);
        float y = Binary.ReadFloat32(pointBytes, 5);
        float z = Binary.ReadFloat32(pointBytes, 9);
        target.localPosition = new Vector3(x, y, z);
    }
}
```

## ガードルール

常に検証してください:

- `byte[]` は null ではありません。
- 各読み取りの前に十分な長さがある。
- バージョンがサポートされています。
- インデックス値は受信側のホワイトリスト内にあります。
- 対象コンポーネントが割り当てられます。

積極的にデバッグを行っている場合を除き、不正なパケットは静かにスキップする必要があります。

## 帯域幅ルール

| パターン | より良い選択肢 |
| --- | --- |
| 多くのブール値 | ビットを 1 バイトにパックします。 |
| 多くの正規化された重み | `byte` または `0..100` には `0..255` 値を使用します。 |
| 既知の範囲を持つ位置 | `ushort` または `short` にクオンタイズします。 |
| 範囲が不明な位置 | `Float32` は、必要な値にのみ使用してください。 |
| 繰り返される文字列 | 代わりに小さな列挙型/インデックスを送信してください。 |

## 互換性

パケット レイアウトの変更は、コンポーネントのプロトコルの変更です。バージョン バイトをインクリメントし、古い送信者が使用されなくなるまで古い読み取りパスを保持します。
