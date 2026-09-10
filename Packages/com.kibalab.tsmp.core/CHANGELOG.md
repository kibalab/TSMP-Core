# Changelog

## 0.2.0-beta.1

- Support installation, native component bindings, encoding, decoding, and Windows Mono Player builds without VRCSDK/UdonSharp.
- Keep SDK-dependent player capture, proxy APIs, and editor tooling conditional while preserving the existing VRChat/UdonSharp paths.
- Use one shared Controller prefab with automatic component, codec, binding, and per-controller resource preparation.
- Preserve existing Controller references through the Legacy prefab with its original GUID.
- Add repeatable native GPU loopback, Player, UdonSharp, SDK world build, and shared workflow validation.
- Update English, Korean, and Japanese installation and setup documentation.
- Use Luma4 0.0.3-beta.3 or newer for the shared SDK-neutral codec template.
- IL2CPP, managed stripping, and an uploaded VRChat client session remain outside the verified configuration.

## 0.1.0

- Promoted the package version out of beta.
- Added the complete TSMP sample assets under `Samples`.

## 0.0.3-beta.4

- Added inspector error boxes for TransSync variable ID collisions.
- Kept detailed TransSync collision logs in the Unity console.
- Fixed UTF-8 encoding for non-ASCII BMP characters in synced strings.

## 0.0.3-beta.3

- Fixed UTF-8 string encoding for three-byte BMP characters, including variation selectors used by emoji sequences.

## 0.0.3-beta.2

- Merged the latest main branch updates into the release branch.
- Added the TransSync values debug canvas sample update.
- Updated localized README and documentation homepage content.

## 0.0.3-beta.1

- Beta release metadata for VPM distribution.
- Includes core runtime, encoder, decoder, setup tooling, shared network behaviours, and codec authoring APIs.

## 0.0.2

- Fixed intermittent editor compile failures during UdonSharp define transitions.

## 0.0.1

- Initial beta package release.

## 1.0.0

- Initial package split with encoder and decoder runtime included in core.
