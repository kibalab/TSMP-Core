# TSMP ドキュメント サイト

このディレクトリには TSMP の Docusaurus ドキュメント サイトが含まれています。

フォルダー名が `Website~` になっているのは、リポジトリを Unity プロジェクト内に置いた場合でも Unity がこのサイトを import しないようにするためです。

## 要件

- Node.js 20 LTS または 22 LTS
- npm

## コマンド

```powershell
npm install
npm start
npm run dev
npm run dev:ko
npm run dev:ja
npm run build
npm run serve:build
```

`npm start` はすべての locale を build し、生成済みサイトを serve します。デプロイ環境と同じ言語切り替えを確認する場合に使います。

`npm run dev` は Docusaurus の開発サーバーを起動します。開発サーバーは 1 つの locale だけを配信するため、言語切り替えの確認には `npm run dev:ko` または `npm run dev:ja` を使います。

`npm run build` は English、한국어、日本語のすべての locale を build します。build 後の言語切り替え確認には `npm run serve:build` を使います。

## デプロイ

GitHub Pages へのデプロイは `.github/workflows/deploy-docs.yml` が担当します。

workflow は Node.js 22 でこのディレクトリを build し、TSMP Unity package を pack して `Website~/build` を GitHub Pages にデプロイします。`main` または `master` への push で、website、package、workflow 関連ファイルが変更された場合に実行され、`workflow_dispatch` による手動実行にも対応しています。
