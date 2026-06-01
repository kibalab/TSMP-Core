# TSMP Core

TSMP(Texture Stream Message Protocol) の Core runtime パッケージです。

このパッケージには Encoder、Decoder、TSMPSetup、Network component、codec 制作用 API が含まれます。標準構成では `TSMP Codec Luma4` などの codec パッケージを一緒にインストールし、`Samples/TSMPController.prefab` をシーンに配置してから `TSMPSetup` で codec を更新し、`Apply Setup` を実行します。

## 要件

- Unity 2022.3
- VRChat Worlds SDK 3.9.0 以降
- Udon behaviour をコンパイルする場合は VRChat Worlds SDK に含まれる UdonSharp

## ドキュメント

https://kibalab.github.io/TSMP-Core/

## リリース状態

このパッケージは beta 段階で、`v0.0.x-beta.x` 形式のタグを使用します。
