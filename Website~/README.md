# TSMP Documentation Site

This directory contains the Docusaurus site for TSMP.

The folder is named `Website~` so Unity ignores it while the repository is checked out under `Assets/TSMP`.

## Requirements

- Node.js 20 LTS or 22 LTS.
- npm.

## Commands

```powershell
npm install
npm start
npm run dev
npm run dev:ko
npm run dev:ja
npm run build
npm run serve:build
```

`npm start` builds every locale and serves the static output. Use it when you
want to test the language switcher exactly like the deployed site.

`npm run dev` starts the Docusaurus development server for editing. Docusaurus
development mode serves one locale bundle at a time, so language switching can
show a local `Page Not Found` page there. Use `npm run dev:ko` or
`npm run dev:ja` when editing a specific locale.

`npm run build` builds every locale. Use `npm run serve:build` after a full build
when you want to test the language switcher across English, Korean, and Japanese.

## Deployment

GitHub Pages deployment is handled by `.github/workflows/deploy-docs.yml`.

The workflow builds this directory with Node.js 22, packs the TSMP Unity
packages, uploads `Website~/build`, and deploys it through GitHub Pages. It runs
on pushes to `main` or `master` that touch the website, package directories, or
workflow files, and it also supports manual `workflow_dispatch`.

The workflow derives the production URL from the GitHub repository:

```yaml
DOCUSAURUS_URL: https://${{ github.repository_owner }}.github.io
DOCUSAURUS_BASE_URL: /${{ github.event.repository.name }}/
DOCUSAURUS_REPOSITORY_URL: https://github.com/${{ github.repository }}
```

For a repository named `tsmp`, this deploys to:

```text
https://<owner>.github.io/tsmp/
```

Package tarballs are published with the website under:

```text
https://<owner>.github.io/tsmp/packages/
```

The generated package index is:

```text
https://<owner>.github.io/tsmp/packages/index.json
```

For a custom domain or a fixed lowercase path, change these workflow environment
variables:

```yaml
DOCUSAURUS_URL: https://kibalabs.github.io
DOCUSAURUS_BASE_URL: /tsmp/
DOCUSAURUS_REPOSITORY_URL: https://github.com/KIBA-Labs/TSMP
```

Keep `DOCUSAURUS_BASE_URL` in sync with the GitHub Pages project path. For a
repository served at `https://kibalabs.github.io/tsmp/`, use `/tsmp/`.
