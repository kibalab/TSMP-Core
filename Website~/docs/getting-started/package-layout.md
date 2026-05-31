---
title: Package Layout
---

# Package layout

Most users should install the default `TSMP` package from the VPM repository. It brings in the required runtime and the default Luma4 codec.

## Default package

`com.kibalab.tsmp` installs the normal TSMP set for a VRChat world.

Use this when you are starting a project or following the quickstart. It is the least error-prone option because Core and Luma4 are installed together.

## Core package

`com.kibalab.tsmp.core` contains the scene components and shared runtime:

- `TSMPSetup`
- `TSMPEncoder`
- `TSMPDecoder`
- `TSMPDebugCanvas`
- TSMP network sync components
- Binding and protocol runtime
- Debug and streaming helpers
- Shared codec base code used by Luma4

Core depends on the default codec package through the normal TSMP distribution because Luma4 is the standard setup path.

## Luma4 codec package

`com.kibalab.tsmp.codec.luma4` contains the default codec, materials, shaders, prefabs, and samples used by the standard setup.

If you are starting a new project, use Luma4 first. Change advanced texture settings only after you have confirmed the scene works with the default path.

## Samples

Samples are provided inside the relevant packages instead of a separate samples package. The main starting point is:

```text
Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab
```

Use the prefab path above when TSMP is installed as packages.
