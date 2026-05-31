---
title: TSMPCodecCatalog API
---

# `TSMPCodecCatalog`

`TSMPCodecCatalog`는 codec package가 자신의 codec prefabs와 package metadata를 `TSMPSetup`에 알려주는 구조입니다.

사용자는 package folder를 직접 찾지 않아도 Setup Codec tab에서 imported codecs를 볼 수 있어야 합니다.

## Catalog fields

| Field | 용도 |
| --- | --- |
| `packageName` | Catalog를 소유한 package ID. 예: `com.kibalab.tsmp.codec.example`. |
| `codecPrefabs` | `TSMPCodec` component가 들어있는 prefabs. |
| `displayName` | 제공되는 경우 사람이 읽는 codec package name. |
| `author` | Setup에 표시되는 read-only author text. |
| `description` | Setup에 표시되는 read-only package description. |
| `version` | Diagnostics용 package version. |

Metadata는 가능하면 package data에서 가져옵니다. Catalog는 Setup이 빠르게 scan할 수 있도록 작고 안정적으로 유지하세요.

## Setup discovery flow

1. `TSMPSetup.RefreshInstalledCodecs()`가 installed catalogs를 scan합니다.
2. Catalog가 codec prefab references를 제공합니다.
3. Setup이 codec root 아래에 codec handlers를 instantiate 또는 reuse합니다.
4. 선택 codec이 encoder와 decoder에 `codecId`를 제공합니다.
5. Setup이 선택 codec의 package metadata를 표시합니다.

## Package authoring rules

- 각 codec prefab은 하나의 primary `TSMPCodec`을 포함하는 것이 좋습니다.
- Codec component는 stable `codecId`를 공개해야 합니다.
- Package metadata는 사용자에게 보이는 간결한 문장으로 작성합니다.
- Optional codec은 Core나 encoder가 concrete C# type을 참조하게 만들면 안 됩니다.

## Troubleshooting

| Problem | Check |
| --- | --- |
| Codec does not appear | Package에 catalog asset이 있고 prefab reference가 할당되어 있는지 확인. |
| Metadata is empty | Package data를 읽을 수 있고 catalog `packageName`이 일치하는지 확인. |
| Wrong codec selected | `Refresh Codecs` 실행, 원하는 codec 선택, `Apply Setup` 실행. |
