---
title: Codec Shaders
---

# Codec shaders

A TSMP codec shader converts between payload bytes and visible frame pixels. The default Luma4 codec is available as a reference path, but custom codecs can use their own materials and shader passes.

## Encode side

An encode shader or material usually receives:

| Input | Purpose |
| --- | --- |
| Payload byte texture or buffer texture | Source bytes arranged by the encoder. |
| Header pixels | Header area already prepared by the core encoder. |
| Codec option bytes | Small codec-specific settings. |
| Block size | Pixel block dimensions for symbol output. |
| Payload size | Number of valid bytes to draw. |

The encode result must leave the header readable and place payload symbols at the layout reported by the codec.

## Decode side

A decode shader or material usually receives:

| Input | Purpose |
| --- | --- |
| Source TSMP frame | Captured texture from camera, OBS, Spout, or another path. |
| Header metadata | Payload size, block size, sample size, and codec option bytes. |
| Payload layout | Start row/block and block count. |
| Byte output texture | Texture that stores recovered bytes for readback. |

The decoder reads only the payload byte count from the header. Unused pixels should not affect the result.

For decode shaders that output recovered bytes to an RGBA byte texture, see the [`TSMPDecodeByteOutput.cginc` scripting API](../scripting-api/decode-byte-output.md).

## Shader include paths

Package shaders should use package-stable include paths. Relative include paths that depend on a scene folder are fragile when the package is moved between project and package locations.

Prefer includes that still work when the package is installed as a VPM package.

## Precision rules

- Keep header sampling exact.
- Avoid filtering on payload symbols.
- Use point sampling for byte recovery.
- Clear or overwrite every output pixel used by the current payload.
- Leave unused output pixels deterministic when possible to avoid visible stale blocks.

## Material properties

Custom codecs should document their shader properties. Common properties include:

| Property | Meaning |
| --- | --- |
| `_MainTex` | Source frame or payload texture. |
| `_PayloadByteTexture` | Intermediate byte texture. |
| `_PayloadSize` | Valid payload byte count. |
| `_BlockSize` | Codec block size. |
| `_SampleSize` | Decode sample size. |
| `_CodecOptionBytes` | Codec-specific option vector. |

Use stable property names so editor setup and runtime code can assign values without special cases.

## Validation

Before shipping a codec shader:

- Encode and decode a deterministic byte sequence.
- Test non-full payload sizes.
- Switch codecs and confirm no stale blocks remain.
- Test with capture scaling disabled.
- Test with the same render texture size users will ship.
