---
title: TSMPCodecCatalog API
---

# `TSMPCodecCatalog`

`TSMPCodecCatalog` lets a codec package advertise its codec prefabs and package metadata to `TSMPSetup`.

Users should see imported codecs in the Setup Codec tab without manually searching package folders.

## Catalog fields

| Field | Purpose |
| --- | --- |
| `packageName` | Package ID that owns the catalog, such as `com.kibalab.tsmp.codec.example`. |
| `codecPrefabs` | Prefabs that contain a `TSMPCodec` component. |
| `displayName` | Human-readable codec package name when provided. |
| `author` | Read-only author text shown by Setup. |
| `description` | Read-only package description shown by Setup. |
| `version` | Package version shown for diagnostics. |

Exact metadata comes from package data when available. The catalog should remain small and stable so Setup can scan it quickly.

## Setup discovery flow

1. `TSMPSetup.RefreshInstalledCodecs()` scans installed catalogs.
2. Catalogs provide codec prefab references.
3. Setup instantiates or reuses codec handlers under the codec root.
4. The selected codec exposes `codecId` to the encoder and decoder.
5. Setup displays package metadata for the selected codec.

## Package authoring rules

- Each codec prefab should contain one primary `TSMPCodec`.
- The codec component should expose a stable `codecId`.
- Package metadata should be user-facing and concise.
- Optional codecs should not require Core or the encoder to reference their concrete C# type.

## Troubleshooting

| Problem | Check |
| --- | --- |
| Codec does not appear | The package contains a catalog asset and the prefab reference is assigned. |
| Metadata is empty | The package has readable package data and catalog `packageName` matches it. |
| Wrong codec selected | Run `Refresh Codecs`, select the intended codec, then run `Apply Setup`. |
