---
title: Codecs
---

# Codecs

Codecs decide how TSMP data is represented as pixels. Start with Luma4, confirm the complete sender-to-receiver path, then consider a denser payload codec if measurements justify it.

## Codec selection

| Codec | Recommended use | Current evidence and limits |
| --- | --- | --- |
| Luma4 | Default starting point and troubleshooting baseline. | Required for every frame header, even when another codec writes the payload. It is a separate package installed alongside Core. |
| Color256 | Conditional choice when more payload density is needed. | Preserved 1,216/1,216 nonblack observations in the saved OBS tests. Validate color conversion, compression and actual useful update rate on your path. |
| RGB16 | Conditional choice with the tested preset and receiver recovery. | Current development-branch Refine recovered 1,212/1,212 nonblack saved OBS observations, including the nine failures in the earlier receiver. Other settings can still reject payloads. |
| RGB20 | Use only on a color-preserving path verified with your payloads. | Not recommended for ordinary OBS H.264/NV12: 0/1,213 nonblack saved OBS observations passed, despite passing PNG/RGB-lossless controls. Higher bitrate or Refine has not established compatibility. |

These are finite recorded-file observations, including repeated/held frames. They are not unique update rates or a guarantee for live VRChat, VRCDN, RTMP/RTSP or HLS playback. Container remuxing preserved the original frames; it did not repair compression damage. CRF28 remains unsuitable for most measured payloads. See [developer testing](../developer/testing.md) for the current validation entry points and the separate live validation requirements.

The RGB16 recovery and recent performance fixes are development-branch changes. Do not assume that a published package with the same base version contains them; record the exact tested revisions when evaluating a setup.

## Install the header and payload codecs

Install **TSMP Core** and **TSMP Codec Luma4** in both ordinary Unity and VRChat. For a color payload, also install its codec package. Luma4 is still needed to read the header that identifies that payload codec.

The current development manifests declare Luma4 as a VPM dependency of the color codecs. Published versions may predate that correction, so confirm that all three packages are installed. With local/disk or Git UPM installs, explicitly supply Core, Luma4 and the selected codec through the project dependencies; numeric package dependencies do not locate GitHub repositories. See [installation](../getting-started/installation.md).

1. Open `TSMPSetup` and refresh codecs.
2. Choose the payload codec in the Codec tab.
3. Click `Apply Setup` if updating the configuration manually.
4. Confirm that sender and receiver use compatible codec options and that Luma4 remains assigned for headers.

## If the decoded frame fails

Before changing advanced settings, check:

- The encoded region is not scaled or filtered.
- OBS or camera output is not applying color correction.
- The receiver is reading the correct input texture.
- The full TSMP frame is visible, including the header area.
- Header and payload CRC diagnostics identify which stage is failing.

If Luma4 does not appear in the Codec tab, install or refresh its package before debugging scene bindings. Refine can recover some damaged symbols within its retry budget; it cannot restore absent frames or colors that the transport has made indistinguishable.
