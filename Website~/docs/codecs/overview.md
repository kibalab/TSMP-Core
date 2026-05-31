---
title: Codecs
---

# Codecs

Codecs decide how TSMP data is represented as pixels.

For normal users, this page is intentionally simple: start with Luma4, make the scene work, then tune texture transport if needed.

## Luma4

Luma4 is the default codec included with TSMP. Use it first unless you have a specific reason to change the texture transport path.

Luma4 is a good starting point because:

- The sample prefab is set up around it.
- It is the baseline path used for troubleshooting.
- It avoids extra package decisions while you are validating the scene.
- It is expected by the user-facing setup guides.

## Selecting Luma4

1. Open `TSMPSetup`.
2. Go to the Codec tab.
3. Choose the Luma4 handler.
4. Click `Apply Setup`.

Sender and receiver must use compatible Luma4 settings. If the sender writes with one codec and the receiver decodes with another, the header or payload may appear damaged.

## If the decoded frame fails

Before changing advanced settings, check:

- The encoded region is not scaled or filtered.
- OBS or camera output is not applying color correction.
- The receiver is reading the correct input texture.
- The full TSMP frame is visible, including the header area.
- Header CRC errors are not being logged.

If Luma4 does not appear in the Codec tab, reinstall or refresh the Luma4 package before debugging scene bindings.
