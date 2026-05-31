---
title: Codec Package Definition
---

# Codec package definition

A custom codec package should be installable on its own and discovered by `TSMPSetup` after import. Core should not reference the codec's concrete C# classes.

## Package identity

Use a package name under:

```text
com.kibalab.tsmp.codec.yourcodec
```

Package metadata should be user-facing:

```json
{
  "name": "com.kibalab.tsmp.codec.yourcodec",
  "displayName": "TSMP Codec YourCodec",
  "version": "1.0.0",
  "author": {
    "name": "KIBA_Labs"
  },
  "description": "A TSMP codec package.",
  "unity": "2022.3",
  "vpmDependencies": {
    "com.kibalab.tsmp.core": "1.x"
  },
  "dependencies": {
    "com.kibalab.tsmp.core": "1.0.0"
  }
}
```

Use VPM dependencies for VRChat Creator Companion installs and Unity dependencies for UPM-style installs when the distribution channel supports them.

## Recommended package contents

| Path | Contents |
| --- | --- |
| `Runtime` | Codec component, runtime helpers, shaders, materials, prefabs. |
| `Editor` | Optional codec editor or setup helpers. |
| `Samples` | Example scene or prefab for users to import. |
| `package.json` | Package metadata. |
| `README.md` | Short user-facing usage summary. |
| `CHANGELOG.md` | Version changes. |
| `LICENSE.md` | License text. |

## Assembly rules

- Runtime assembly should depend on `K13A.TSMP.Core`.
- Editor assembly should depend on runtime assembly and Unity editor assemblies.
- UdonSharp-compatible scripts should be in assemblies that UdonSharp can compile.
- Avoid references from Core to optional codec assemblies.

## Catalog rule

Add a `TSMPCodecCatalog` asset or prefab reference that lists codec prefabs. Setup uses it to populate the Codec tab.

The codec prefab should include:

- One `TSMPCodec` component.
- Encode/decode materials.
- Any codec-specific settings with clear inspector names.

## Versioning rule

Changing byte layout, shader symbol mapping, or `codecId` compatibility is a breaking codec change. Use semantic versioning and explain migration in the changelog.
