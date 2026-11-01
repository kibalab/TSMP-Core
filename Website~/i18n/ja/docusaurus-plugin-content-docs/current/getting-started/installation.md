---
title: インストール
---

# インストール

通常の Unity アプリか VRChat ワールドかに応じてインストール方法を選びます。両方で同じコンポーネントのソースを使うため、Standalone 専用ブランチは不要です。

## 要件

- Unity 2022.3 LTS。通常の Unity の検証には Windows 上の Unity 2022.3.22f1 を使用しています。
- Core と実際のコーデックパッケージ。まず Luma4 を使用してください。
- コーデックのシェーダーと非同期 GPU readback に対応したグラフィックスデバイス。
- **VRChat ワールドの場合のみ** Worlds SDK 3.9.0 以降と、同梱の UdonSharp。

SDK がない場合、コンポーネントは MonoBehaviour として動作します。Animator ベースのヒューマノイドキャプチャ、Transform、TransSync フィールド、RPC は Unity Editor と Windows Player で利用できます。VRChat プレイヤー情報の取得には SDK が必要ですが、受信したポーズを用意したアバターリグに適用する処理にはプレイヤー API は不要です。

## VRChat: VPM

VRChat Creator Companion または VPM 対応マネージャーに次のリポジトリを追加します。

```text
https://vpm.kiba.red/
```

**TSMP Core** と **TSMP Codec Luma4**、または両方を含む TSMP バンドルをインストールします。この経路では VPM メタデータの Worlds SDK 依存を維持しています。

パッケージの import と SDK のスクリプトコンパイルが完了したら、下記の共通コントローラーを使用します。Udon コンポーネントとバインディングは TSMP が自動で準備します。

## 通常の Unity: UPM

通常の Unity 対応を含む Core と Luma4 を使用してください。古いパッケージでは UPM メタデータにも Worlds SDK への依存が残っている場合があります。その場合、SDK のシンボルを削除するだけでは解決しません。

検証済みのインストール方法は Unity Package Manager の **Add package from disk** です。

1. Core と Luma4 のパッケージソースを用意します。
2. Core の `Packages/com.kibalab.tsmp.core/package.json` を選択します。
3. Luma4 の `Packages/com.kibalab.tsmp.codec.luma4/package.json` を選択します。
4. パッケージ解決とスクリプトのコンパイルが完了するまで待ちます。

プロジェクトの `Packages/manifest.json` にローカルパッケージのフォルダーを指定することもできます。実際の配置に合わせてパスを変更してください。

```json
{
  "dependencies": {
    "com.kibalab.tsmp.core": "file:../../TSMP-Core/Packages/com.kibalab.tsmp.core",
    "com.kibalab.tsmp.codec.luma4": "file:../../TSMPCodec-Luma4/Packages/com.kibalab.tsmp.codec.luma4"
  }
}
```

既存の dependencies に追加し、manifest 全体を置き換えないでください。UPM は Unity モジュールの依存を解決しますが、VRCSDK は追加しません。VPM の SDK 要件と UPM の依存は別々です。

SDK のないプロジェクトで `UDONSHARP` や `COMPILER_UDONSHARP` を手動定義しないでください。既存プロジェクトから SDK を削除した場合は、残った SDK 関連のカスタムシンボルも削除します。

## コントローラーを配置する

通常の Unity と VRChat のどちらでも `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab` をシーンにドラッグします。変換メニューや手動の Apply Setup は不要です。Setup が必要なコンポーネントを作成し、インストール済みのコーデックを自動で検出します。

共通サンプルは最初にローカルループバック用として Encoder 出力を Decoder 入力へ接続します。外部ストリームを受信するときは Decoder Source Texture を指定してください。SDK の有無で選択した入力やコーデックは変わりません。

作業用テクスチャとマテリアルは `Assets/TSMPGenerated` に自動生成されます。シーンと一緒にバージョン管理へ含めてください。インストール済みパッケージを変更せず、各 Controller の出力を独立させます。

パッケージの Udon Program Asset は SDK 環境でのみ使用します。共通 Controller は SDK なしで利用できますが、他のデモシーンにはアバター、ストリーミングプラグイン、VRChat コンポーネントなどの依存が必要な場合があります。

## 開始プロファイル {#starting-profile}

最初のアバター検証候補は Core 1.0 + Luma4 1.0、共通 Controller、1280x720、block size 8、Full Humanoid 22レコード、指・名前なし、`maxPlayers=16` です。目標30Hzから始め、実際の送信率と適用率を測定します。これは基準候補であり、実際の16人のリアルタイム性能を認証するものではありません。

この設定の Luma4 payload 容量は6,720バイトです。キャッシュのない初期状態で16人全員の root を含むアバターフィールド1個は、NetworkFrame framing を含め2,495バイトです。他のフィールドと RPC は追加容量を使います。Avatar Pose インスペクターは初期推定量、エンコーダー容量とポーズのみの余裕を表示します。キャッシュで root のバイト数を減らせても、初期容量の条件は残ります。設定上限80人や計算上の収容上限は検証済み人数ではありません。

`TSMPSetup` の既定解像度は1280x720です。単独追加した Encoder は、出力テクスチャや Setup による設定前には640x360を使います。容量の比較では実際の出力サイズを確認してください。

2026-10-27の検証環境は Windows、Unity 2022.3.22f1、D3D11、RTX 4090です。Native GPU loopback、Windows Mono、Worlds 3.10.4-beta.2 の実際の Editor Udon VM とローカル world bundle build を確認しました。実クライアントのワールド入場は認証・UI アクセスにより未確認です。実際の16人のアバター負荷、圧縮された外部転送、長時間運転、低スペック、Quest、IL2CPP は未検証です。loopback、VM 実行と world build はそれぞれの段階の証拠です。

## 既存のシーン

既存 Controller のプレハブ参照とオーバーライドを維持するため、以前のプレハブは元の GUID のまま `Samples/Legacy/TSMPControllerLegacy.prefab` に保持します。新しいインスタンスには共通の `Samples/TSMPController.prefab` を使用します。既存 SDK シーンの変換コマンドは不要です。ただし、以前のシーンを SDK のないプロジェクトへ移す場合は、共通 Controller を使用し、他の任意依存も確認してください。Legacy プレハブ自体は SDK なしで使う共通サンプルではありません。

## Player 設定

検証済みの構成は **Windows x64、Mono、Managed Stripping 無効**です。他のアプリにフォーカスが移っても送受信を続ける場合は **Run In Background** を有効にしてください。

IL2CPP と stripping はアプリごとに別途検証が必要です。TransSync フィールドの検索と RPC 呼び出しにはリフレクションを使用します。stripping を有効にする場合、リフレクションからのみ使用するフィールドやメソッドを保持してください。Mono ビルドの成功は IL2CPP の成功を意味しません。

次は [クイックスタート](quickstart.md) に進みます。
