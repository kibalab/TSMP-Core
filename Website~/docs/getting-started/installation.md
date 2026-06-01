---
title: Installation
---

# Installation

Install TSMP through VRChat Creator Companion or another VPM-compatible package manager.

For normal VRChat world projects, use VPM. It installs TSMP with the package metadata that VRChat tooling expects.

## Requirements

Required:

- Unity 2022.3 LTS.

Optional, depending on how you use TSMP:

- VRChat Worlds SDK 3.9.0 or newer when building a VRChat world.
- UdonSharp when running TSMP inside VRChat.

The VRChat SDK is optional because TSMP package assets can be opened outside a VRChat world project. It becomes required when you want to compile UdonSharp behaviours or upload a VRChat world.

## VPM repository

Add this VPM repository:

```text
https://vpm.kiba.red/
```

Then install:

```text
TSMP
```

The default package installs Core and the Luma4 codec. Luma4 is the recommended first codec and is used by the sample controller prefab.

## UPM package IDs

If you install through Unity Package Manager, use the same package IDs:

```json
"com.kibalab.tsmp": "0.0.3-beta.2"
```

The default package depends on:

```json
"com.kibalab.tsmp.core": "0.0.3-beta.2",
"com.kibalab.tsmp.codec.luma4": "0.0.3-beta.2"
```

Use VPM for VRChat projects unless you have a specific package-management reason to use UPM directly.

## After installation

Confirm these assets are available:

- `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab`
- The Luma4 codec package.
- `TSMPSetup`, `TSMPEncoder`, and `TSMPDecoder` components in Add Component.

If any of these are missing, refresh the VCC package list and confirm the TSMP repository was added correctly.
