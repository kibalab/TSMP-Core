---
title: GPU skinning for a humanoid receiver
---

Use this for a **visual copy** of a performer in a world. The sender uses the regular `TSMPNetworkHumanoidPoseSync`; the receiver renders a baked MeshRenderer from the [shader channel](./shader-receive.md). It does not move scene bones, colliders or attachments. Keep the CPU receiver when other components need those transforms.

## Bake the receiver

1. Enable a **Shader Channel** on the receiving decoder. Note its channel (0–3) and the **sender's** humanoid pose Network ID. The decoder and codec must already receive valid frames.
2. In Edit Mode, place the receiver's valid Humanoid Animator in its rest pose. Select a child SkinnedMeshRenderer. Its model must have **Read/Write Enabled**, exposed bone transforms and at most four weights per vertex. All weighted bones must be at or below Hips. Use positive uniform scale on the Animator and its parents, and on the transforms between the Animator and Hips.
3. Open **TSMP → Bake Humanoid GPU Skin**. Choose the Animator and renderer, enter the sender ID and decoder channel, and set **Motion Margin** to cover the performer's travel on each side of the mesh (Animator-local units). With World root motion, put the receiver near the stage. Unity culls against these fixed bounds, not the animated vertices.
4. Click **Bake GPU Skin** and choose a new asset under Assets. Save the scene. The tool creates a MeshRenderer under the Animator, disables the original renderer and leaves the source mesh and materials untouched. Repeat for additional meshes. Keep the generated object at its identity local transform; do not mark it for static batching.
5. Play the sender animation. Both Local and World Hips motion are supported. No receiving pose component is required. Disable a redundant CPU receiver or set its receive mode to **None** if it serves no other purpose.

The generated asset contains the mesh, rig data, palettes and materials. The default opaque Built-in shader copies the albedo texture, tiling/offset and tint, **not** the full source shader. Re-bake after changing rig hierarchy, weights or rest pose. To revert, remove the generated object and re-enable the original renderer.

## What is animated?

The two GPU passes decode the current v4 `humanoid.pose` field and build per-bone matrices; the material deforms positions, normals and tangents. There are no per-bone Udon writes or skinning readbacks. The decoder still performs its normal frame validation and readback. The two palette textures update each render frame, per baked mesh; this is not a measured frame-time guarantee.

Selected humanoid rotations are applied discretely. Unselected and non-humanoid joints keep their baked local transform relative to their animated parent. If an entire field is omitted by SendOnChange, its previous value is retained. When the channel/field is unavailable or the packet format is unsupported, the mesh shows its rest geometry. This path does **not** decode the separate VRChat player-pool packet, simulate secondary motion, apply BlendShapes or perform CPU receive interpolation.

## Custom materials

Use the generated skin palette on your material's `_TSMPSkinPalette` 2D property, disable batching, and apply skinning in every pass that draws geometry (including shadows). UV3 and UV4 on the **baked copy** contain bone indices and weights. Call the helper before transforming vertices to clip space:

```hlsl
#pragma target 3.5
#include "Packages/com.kibalab.tsmp.core/Shaders/TSMPSkinning.cginc"

void vert(inout appdata_full v)
{
    float3 p = v.vertex.xyz;
    TSMPSkinVertex(p, v.normal, v.tangent, v.texcoord2, v.texcoord3);
    v.vertex.xyz = p;
}
```
