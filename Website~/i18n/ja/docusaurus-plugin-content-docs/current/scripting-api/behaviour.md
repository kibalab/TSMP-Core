---
title: TSMPBehaviour
---

# TSMPBehaviour

名前空間: `K13A.TSMP`

`TSMPBehaviour` は、TSMP ランタイム コンポーネントの基本型です。

コンパイル シンボルに応じて、次のものを継承します。

- `UdonSharpBehaviour` `UDONSHARP` が定義されている場合。
- `MonoBehaviour` それ以外の場合。

これにより、TSMP コンポーネントは、UdonSharp ランタイムおよび Unity エディター/ネイティブ ランタイム用にコンパイルしながら 1 つのソース シェイプを共有できます。

## 静的ブリッジメソッド

| 方法 | 使用 |
| --- | --- |
| `SetProgramVariable(...)` | Udon または Mono ターゲットにフィールドを設定します。 |
| `GetProgramVariable(...)` | Udon または Mono ターゲットからフィールドを読み取ります。 |
| `SendCustomEvent(...)` | Udon または Mono ターゲットでイベント/メソッドを呼び出します。 |

フレームワーク コードが Unity C# コンポーネントと Udon の動作の両方で動作する必要がある場合は、これらのメソッドを使用します。

通常のカスタム コンポーネントでは、`this` での直接フィールド アクセスを優先します。ブリッジ呼び出しは、エンコーダー、デコーダー、バインディング、およびディスパッチ コードでより便利です。

## ロギングヘルパー

`TSMPBehaviour` は、ランタイム コンポーネントで使用される保護されたレート制限されたログ ヘルパーを提供します。

| 方法 | 目的 |
| --- | --- |
| `ResetTSMPLogBudget(int budget)` | 許可されるデバッグ/エラー メッセージの数をリセットします。 |
| `LogTSMPError(string prefix, string error, bool enabled)` | 予算が残っている間はエラーが発生します。 |
| `LogTSMPWarning(string prefix, string error, bool enabled, float intervalSeconds)` | 予算と間隔を制御して警告を発します。 |
| `LogTSMPRateLimitedWarning(string prefix, string error, float intervalSeconds)` | インターバル制御のみで警告を発します。 |

これらは、VRChat ログを大量に生成せずに、実行時に表示される診断が必要な TSMP コンポーネントを追加するときに使用します。

## 直接導出する場合

ユーティリティ コンポーネント、デバッグ UI、アバター リグ ヘルパーなど、ネットワーク ID によって同期されない TSMP ランタイム コンポーネントについては、`TSMPBehaviour` から直接派生します。

コンポーネントが TSMP ネットワーク メッセージを送受信するときに `TSMPNetworkBehaviour` から派生します。
