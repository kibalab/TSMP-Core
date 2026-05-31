---
title: TSMPSetup API
---

# `TSMPSetup`

`TSMPSetup` は、エンコーダー、デコーダー、コーデック ハンドラー、バインディング、レンダー テクスチャ、およびマテリアルが互いに一致するようにシーンを構成します。

ほとんどのユーザーはインスペクターを通じてそれを操作します。スクリプトとエディタ ツールは、そのパブリック メソッドを呼び出して同じ構成を更新できます。

## 主な参考文献

| 分野 | 目的 |
| --- | --- |
| `encoder` | `TSMPEncoder` を設定します。 |
| `decoder` | `TSMPDecoder` を設定します。 |
| `sourceTexture` | 必要に応じて送信側で使用されるテクスチャ。 |
| `decoderSourceTexture` | デコーダによって読み取られたテクスチャ。 |
| `preferEncoderOutputAsDecoderSource` | エンコーダ出力をループバック テストのデコーダ ソースとして使用します。 |
| `encoderOutput` | エンコーダに割り当てられた出力レンダー テクスチャ。 |
| `payloadByteTexture` | デコーダに割り当てられたバイト テクスチャ。 |

## フレーム設定

| 分野 | 目的 |
| --- | --- |
| `width` / `height` | テクスチャが提供されていない場合のフォールバック フレーム サイズ。 |
| `blockSize` | 互換性のあるコーデックで使用されるピクセル ブロック サイズ。 |
| `sampleSize` | ヘッダー/デコードのサンプル サイズ。 |
| `flipY` | デコーダ側の画像反転。 |

出力テクスチャとソース テクスチャが割り当てられている場合、TSMPSetup は手動フィールドを繰り返す必要なく、それらのテクスチャからサイズを導出できます。

## コーデックの検出

| 分野 | 目的 |
| --- | --- |
| `autoDiscoverCodecs` | インストールされているコーデック カタログとプレハブ エントリを検索します。 |
| `codecPrefabs` | シーンで使用できるコーデック プレハブ。 |
| `selectedCodecIndex` | 選択されたコーデック エントリ。 |
| `codecInstanceRoot` | コーデック インスタンスの親オブジェクト。 |

[コーデック] タブには、コーデック パッケージがカタログを公開するときに、パッケージ名、作成者、説明などのパッケージ メタデータを表示できます。

## レンダリングテクスチャの設定

| 分野 | 目的 |
| --- | --- |
| `resizeFrameRenderTextures` | セットアップの寸法に合わせてフレーム テクスチャのサイズを変更します。 |
| `resizeByteRenderTextures` | デコーダのリードバックのためにバイト テクスチャのサイズを変更します。 |
| `autoSizeByteTexture` | ペイロード容量からバイト テクスチャ サイズを計算します。 |
| `roundByteTextureWidthToPowerOfTwo` | バイト テクスチャ幅を 2 ​​のべき乗に対応させます。 |
| `byteTextureSafetyPixels` | エッジのオーバーフローを避けるためにピクセルを追加します。 |

## `ApplyNow()`

```csharp
public void ApplyNow()
```

すべてのセットアップ オプションをすぐに適用します。コーデック インスタンスを更新し、エンコーダ/デコーダ参照を割り当て、ネットワーク バインディングを再構築し、テクスチャを更新し、コーデック設定を書き込みます。

次の後に実行します。

- `TSMPNetworkBehaviour` コンポーネントを追加または削除します。
- ネットワークIDを変更する。
- コーデックの選択を変更します。
- 出力テクスチャまたはソース テクスチャの変更。
- パッケージのインポートを変更します。

## `EncodeEncoderNow()`

```csharp
public void EncodeEncoderNow()
```

構成されたエンコーダで `EncodeNow()` を呼び出します。これは主に、エディターのタイム フレーム プレビューのためのデバッグ アクションです。

## `RefreshInstalledCodecs()`

```csharp
public void RefreshInstalledCodecs()
```

インストールされているコーデック カタログをスキャンし、コーデック リストを更新します。コーデック パッケージをインポートまたは削除した後に使用します。

## プレハブのセットアップ

通常のシーンの場合は、次から開始します。

```text
Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab
```

プレハブを配置し、トランスポート パスにテクスチャを割り当て、コーデックを選択して、`Apply Setup` を実行します。
