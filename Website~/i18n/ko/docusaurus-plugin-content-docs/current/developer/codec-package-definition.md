---
title: Codec Package Definition
---

# Codec package definition

Custom codec package는 독립적으로 install 가능해야 하며 import 후 `TSMPSetup`이 발견할 수 있어야 합니다. Core는 codec의 concrete C# class를 참조하면 안 됩니다.

## Package identity

Package name은 다음 prefix 아래를 사용하세요.

```text
com.kibalab.tsmp.codec.yourcodec
```

Package metadata는 사용자에게 보이는 정보입니다.

```json
{
  "name": "com.kibalab.tsmp.codec.yourcodec",
  "displayName": "TSMP Codec YourCodec",
  "version": "0.0.3-beta.3",
  "author": {
    "name": "KIBA_Labs"
  },
  "description": "A TSMP codec package.",
  "unity": "2022.3",
  "vpmDependencies": {
    "com.kibalab.tsmp.core": ">=0.0.1"
  },
  "dependencies": {
    "com.kibalab.tsmp.core": "0.0.3-beta.3"
  }
}
```

VRChat Creator Companion install에는 VPM dependencies를, 배포 채널이 지원하는 경우 UPM-style install에는 Unity dependencies를 사용합니다.

## Recommended package contents

| Path | Contents |
| --- | --- |
| `Runtime` | Codec component, runtime helpers, shaders, materials, prefabs. |
| `Editor` | Optional codec editor 또는 setup helpers. |
| `Samples` | 사용자가 import할 example scene 또는 prefab. |
| `package.json` | Package metadata. |
| `README.md` | 짧은 user-facing usage summary. |
| `CHANGELOG.md` | Version changes. |
| `LICENSE.md` | License text. |

## Assembly rules

- Runtime assembly는 `K13A.TSMP.Core`에 의존합니다.
- Editor assembly는 runtime assembly와 Unity editor assemblies에 의존합니다.
- UdonSharp-compatible scripts는 UdonSharp가 compile할 수 있는 assembly에 있어야 합니다.
- Core에서 optional codec assemblies를 참조하지 마세요.

## Catalog rule

Codec prefabs를 나열하는 `TSMPCodecCatalog` asset 또는 prefab reference를 추가하세요. Setup은 이것으로 Codec tab을 채웁니다.

Codec prefab은 다음을 포함하는 것이 좋습니다.

- 하나의 `TSMPCodec` component.
- Encode/decode materials.
- 명확한 inspector 이름을 가진 codec-specific settings.

## Versioning rule

Byte layout, shader symbol mapping, `codecId` compatibility 변경은 breaking codec change입니다. Semantic versioning을 사용하고 changelog에 migration을 설명하세요.
