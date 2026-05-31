[한국어](README.md) | [English](README.en.md) | **日本語**

---

# VRChat VPM Package Template for KIBALAB

VRChat Creator Companion (VCC) / VRChat Package Manager (VPM) パッケージ配布用のテンプレートです。

**タグ(リリース)をプッシュすると**、GitHub Actionsが自動的に：
1. リリースを作成 (zip + unitypackage + package.json)
2. VPMバックエンドにパッケージ情報を登録
3. 即座に[vpm.kiba.red](https://vpm.kiba.red)に反映

---

## 要件

### 1) パッケージ構造 (UPM/VPM標準)

```
Packages/<PACKAGE_ID>/
├── package.json
├── Runtime/
├── Editor/
└── package-media/        # (オプション) サムネイル画像
    └── thumbnail.png
```

例：
```
Packages/com.kibalab.mypackage/package.json
```

### 2) package.json 必須フィールド

```json
{
  "name": "com.kibalab.mypackage",
  "displayName": "My Package",
  "version": "1.0.0",
  "description": "パッケージの説明",
  "author": {
    "name": "Your Name",
    "email": "your@email.com",
    "url": "https://your-site.com"
  },
  "vpmDependencies": {
    "com.vrchat.worlds": "3.x.x"
  }
}
```

---

## セットアップ方法

### 1) Repository Variables

GitHubリポジトリ → **Settings** → **Secrets and variables** → **Actions** → **Variables**

| Variable | 説明 | 例 |
|----------|------|-----|
| `PACKAGE_NAME` | パッケージフォルダ名 | `com.kibalab.mypackage` |
| `VPM_BACKEND_URL` | VPMバックエンドURL | `https://vpm.kiba.red` |

### 2) Repository Secrets

GitHubリポジトリ → **Settings** → **Secrets and variables** → **Actions** → **Secrets**

| Secret | 説明 |
|--------|------|
| `VPM_API_KEY` | VPMバックエンドAPIキー (管理者に問い合わせ) |

---

## 使用方法

### 新しいパッケージの作成

1. **Use this template**で新しいリポジトリを作成
2. `Packages/`フォルダ配下にパッケージIDでフォルダを作成
3. `package.json`を作成
4. Repository Variables/Secretsを設定

### リリースのデプロイ

1. `package.json`の`version`を更新
2. コミット & プッシュ
3. 同じバージョンでタグを作成 & プッシュ

```bash
# バージョン更新後にコミット
git add Packages/com.kibalab.mypackage/package.json
git commit -m "Bump version to 1.0.1"
git push

# タグを作成してプッシュ
git tag 1.0.1
git push origin 1.0.1
```

> タグのバージョンとpackage.jsonのバージョンが一致している必要があります。（`v1.0.1`または`1.0.1`形式の両方をサポート）

---

## サムネイル画像

VPMフロントエンドに表示されるサムネイルを設定できます。

### 方法1: パッケージ内サムネイル (推奨)
```
Packages/<PACKAGE_ID>/package-media/thumbnail.png
```

### 方法2: リポジトリルートサムネイル
```
.github/vpm-thumbnail.png
```

**推奨仕様：**
- 形式: PNG
- サイズ: 512x512または16:9比率
- 容量: 500KB以下

---

## ワークフロー構造

### Reusable Workflow (中央管理)

すべてのパッケージリポジトリが`vpm-package-template`のワークフローを参照します。
中央ワークフローを変更すると**すべてのパッケージリポジトリに自動適用**されます。

```
vpm-package-template/.github/workflows/
├── vpm-release.yml    # 再利用可能なワークフロー (実際のロジック)
└── release.yml        # 使用例

各パッケージリポジトリ/.github/workflows/
└── release.yml        # 中央ワークフローを呼び出し (16行)
```

### 各パッケージリポジトリのrelease.yml

```yaml
name: Build Release

on:
  workflow_dispatch:
  push:
    tags:
      - '*'

permissions:
  contents: write

jobs:
  release:
    uses: kibalab/vpm-package-template/.github/workflows/vpm-release.yml@main
    with:
      package_name: ${{ vars.PACKAGE_NAME }}
      vpm_backend_url: ${{ vars.VPM_BACKEND_URL || 'https://vpm.kiba.red' }}
    secrets:
      VPM_API_KEY: ${{ secrets.VPM_API_KEY }}
```

### ワークフロー動作

1. **ビルド**
   - `Packages/<PACKAGE_NAME>`フォルダをZIPに圧縮
   - `.unitypackage`ファイルを生成

2. **GitHub Releaseの作成**
   - ZIP、unitypackage、package.jsonを添付

3. **VPMバックエンドへの登録**
   - パッケージ情報をバックエンドAPIに送信
   - サムネイルURLを自動検出して登録

---

## トラブルシューティング

### ワークフロー失敗: "Tag does not match version"
- `package.json`の`version`とGitタグが一致しているか確認
- タグは`1.0.0`または`v1.0.0`形式のいずれも可能

### パッケージがリストに表示されない
- GitHub Actionsログでバックエンドのレスポンスを確認
- `VPM_BACKEND_URL`と`VPM_API_KEY`の設定を確認
- バックエンド管理者にAPIキーの有効性を問い合わせ

### サムネイルが表示されない
- ファイルパスが正確か確認
- 画像が`main`ブランチにプッシュされているか確認
- Raw URLにアクセスできるか確認

---

## 関連リンク

- [VPMパッケージリスト](https://vpm.kiba.red)
- [VCCに追加](https://vpm.kiba.red/vcc)
