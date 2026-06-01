[한국어](README.ko.md) | [English](README.en.md) | **日本語**

# TSMP Core

TSMP(Trans Sync Media Protocol) は、VRChat ワールド内でネットワーク状態、RPC、アバターのポーズ、Animator、Timeline などのランタイム データをテクスチャ ストリームで送るためのオープンソース パッケージです。

Core パッケージには、シーンに配置する基本ランタイムとセットアップ機能が含まれます。実際のピクセル エンコード方式は codec パッケージが担当します。標準構成では Luma4 codec を一緒にインストールしてください。

## インストール

VRChat Creator Companion で VPM リポジトリを追加します。

```text
https://vpm.kiba.red/
```

その後、`TSMP Core` と `TSMP Codec Luma4` をインストールします。

## クイック スタート

1. `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab` をシーンに配置します。
2. 同期したいオブジェクトに必要な `TSMPNetwork*` コンポーネントを追加します。
3. `TSMPSetup` で `Refresh Codecs` を押し、使用する codec を選択します。
4. `Apply Setup` を実行し、Encoder、Decoder、codec handler、binding table を更新します。
5. Encoder の出力 RenderTexture を配信し、同じ TSMP 映像を Decoder の入力 RenderTexture に入れます。

## 主な機能

- TSMP Encoder / Decoder
- TSMPSetup シーン設定ツール
- `[TransSync]` によるフィールド同期
- `SendTransRPC(methodName, target)` による TSMP RPC
- Transform、Rigidbody、Humanoid Pose、VRChat Avatar Pose、Animator、Timeline、BlendShape 同期コンポーネント
- codec パッケージの自動検出と選択 UI
- カスタム codec パッケージ用の共通ランタイム、shader include、catalog asset

## ドキュメント

ユーザー ガイドと開発者向けドキュメントはこちらです。

https://kibalab.github.io/TSMP-Core/

## リリース状態

TSMP は現在 beta 段階です。パッケージ バージョンと Git タグは `v0.0.x-beta.x` 形式を使用します。

## ライセンス

MIT License. Copyright (c) 2026 KIBA_Labs.
