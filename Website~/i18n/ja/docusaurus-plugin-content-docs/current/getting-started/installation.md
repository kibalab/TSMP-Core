---
title: インストール
---

# インストール

VRChat Creator Companion または別の VPM 互換パッケージ マネージャーを介して TSMP をインストールします。

通常の VRChat ワールド プロジェクトの場合は、VPM を使用します。 TSMP ツールが期待するパッケージ メタデータを使用して TSMP をインストールします。

＃＃ 要件

必須：

- Unity 2022.3 LTS。

TSMP の使用方法に応じてオプションです。

- VRChat ワールドを構築する場合は Worlds SDK 3.9.0 以降。
- UdonSharp VRChat 内で TSMP を実行する場合。

TSMP パッケージ アセットは VRChat ワールド プロジェクトの外部で開くことができるため、VRChat SDK はオプションです。 UdonSharp ビヘイビアーをコンパイルする場合や、VRChat ワールドをアップロードする場合に必要になります。

## VPM リポジトリ

この VPM リポジトリを追加します。

```text
https://vpm.kiba.red/
```

次に、以下をインストールします。

```text
TSMP
```

デフォルトのパッケージでは、Core と Luma4 コーデックがインストールされます。 Luma4 は推奨される最初のコーデックであり、サンプル コントローラー プレハブで使用されます。

## UPM パッケージ ID

Unity Package Manager を通じてインストールする場合は、同じパッケージ ID を使用します。

```json
"com.kibalab.tsmp": "0.0.3-beta.1"
```

デフォルトのパッケージは以下に依存します。

```json
"com.kibalab.tsmp.core": "0.0.3-beta.1",
"com.kibalab.tsmp.codec.luma4": "0.0.3-beta.1"
```

UPM を直接使用する特定のパッケージ管理理由がない限り、VRChat プロジェクトには VPM を使用します。

## インストール後

これらのアセットが利用可能であることを確認します。

- `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab`
- Luma4 コーデック パッケージ。
- コンポーネントの追加の `TSMPSetup`、`TSMPEncoder`、および `TSMPDecoder` コンポーネント。

これらのいずれかが欠落している場合は、VCC パッケージ リストを更新し、TSMP リポジトリが正しく追加されたことを確認します。
