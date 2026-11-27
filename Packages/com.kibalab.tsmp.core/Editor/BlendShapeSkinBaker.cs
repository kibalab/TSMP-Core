using System;
using System.Collections.Generic;
using K13A.TSMP.Udon;
using UnityEngine;
using Object = UnityEngine.Object;

namespace K13A.TSMP.Editor
{
    internal static class BlendShapeSkinBaker
    {
        private const int Width = 256;

        private struct Delta
        {
            internal Vector3 Position, Normal, Tangent;
            internal int Slot;
        }

        internal static void Apply(TSMPNetworkBlendShapesSync sender, int id, int channel, Mesh source, Mesh baked,
            List<int> originalIndices, Matrix4x4 meshToRig, Material[] materials, List<Object> owned)
        {
            if (id <= 0 || id > ushort.MaxValue) throw new ArgumentException("A BlendShape sender needs a Network ID between 1 and 65535.");
            var renderer = sender.targetRenderer ? sender.targetRenderer : sender.GetComponent<SkinnedMeshRenderer>();
            Mesh schema = renderer ? renderer.sharedMesh : null;
            if (!schema) throw new ArgumentException("Assign the sender's mesh to the BlendShape sender.");
            int[] selected = sender.blendShapeIndices;
            if (selected == null || selected.Length == 0 || selected.Length > 255)
                throw new ArgumentException("Select between 1 and 255 keys on the BlendShape sender.");
            var normalMatrix = meshToRig.inverse.transpose;
            var normals = source.normals;
            var tangents = source.tangents;
            int vertices = baked.vertexCount;
            var entries = new List<Delta>[vertices];
            var travel = new Vector3[vertices];
            var slots = new List<Color>();
            var positions = new Vector3[source.vertexCount];
            var dn = new Vector3[source.vertexCount];
            var dt = new Vector3[source.vertexCount];
            var prevP = new Vector3[source.vertexCount];
            var prevN = new Vector3[source.vertexCount];
            var prevT = new Vector3[source.vertexCount];
            var used = new HashSet<int>();
            int total = 0;
            for (int record = 0; record < selected.Length; record++)
            {
                int wireIndex = selected[record];
                if (wireIndex < 0 || wireIndex >= schema.blendShapeCount || wireIndex > ushort.MaxValue || !used.Add(wireIndex))
                    throw new ArgumentException("Invalid or duplicate selected sender index: " + wireIndex);
                string name = schema.GetBlendShapeName(wireIndex);
                int shape = source.GetBlendShapeIndex(name);
                if (shape < 0) throw new ArgumentException("Receiver mesh is missing the selected BlendShape: " + name);
                int frames = source.GetBlendShapeFrameCount(shape);
                float start = 0f;
                for (int frame = 0; frame < frames; frame++)
                {
                    float end = source.GetBlendShapeFrameWeight(shape, frame);
                    if (!(end > start) || float.IsInfinity(end))
                        throw new ArgumentException("BlendShape frames must have increasing positive weights: " + name);
                    if (slots.Count >= SystemInfo.maxTextureSize)
                        throw new ArgumentException("Too many selected BlendShape frames for a weight texture.");
                    bool last = frame == frames - 1;
                    float maxFactor = Mathf.Max(0f, (100f - start) / (end - start));
                    if (!last) maxFactor = Mathf.Min(1f, maxFactor);
                    source.GetBlendShapeFrameVertices(shape, frame, positions, dn, dt);
                    int slot = slots.Count;
                    slots.Add(new Color((record << 16) | wireIndex, start, end, last ? 1 : 0));
                    for (int v = 0; v < vertices; v++)
                    {
                        int src = originalIndices == null ? v : originalIndices[v];
                        Vector3 p = positions[src], n = dn[src], t = dt[src];
                        if (frame != 0) { p -= prevP[src]; n -= prevN[src]; t -= prevT[src]; }
                        if (p.sqrMagnitude == 0f && n.sqrMagnitude == 0f && t.sqrMagnitude == 0f) continue;
                        if (++total > Width * Math.Min(16384, SystemInfo.maxTextureSize) / 3)
                            throw new ArgumentException("Selected expression deltas exceed the texture budget; bake fewer keys or a smaller face mesh.");
                        p = meshToRig.MultiplyVector(p);
                        if (n.sqrMagnitude != 0f && src < normals.Length)
                            n = normalMatrix.MultiplyVector(n) / Mathf.Max(1e-8f, normalMatrix.MultiplyVector(normals[src]).magnitude);
                        else n = Vector3.zero;
                        if (t.sqrMagnitude != 0f && src < tangents.Length)
                            t = meshToRig.MultiplyVector(t) / Mathf.Max(1e-8f, meshToRig.MultiplyVector(tangents[src]).magnitude);
                        else t = Vector3.zero;
                        (entries[v] ??= new List<Delta>()).Add(new Delta { Position = p, Normal = n, Tangent = t, Slot = slot });
                        travel[v] += maxFactor * new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z));
                    }
                    (prevP, positions) = (positions, prevP);
                    (prevN, dn) = (dn, prevN);
                    (prevT, dt) = (dt, prevT);
                    start = end;
                }
            }
            if (total == 0) return;

            int height = (3 * total + Width - 1) / Width;
            var data = new Color[Width * height];
            var uv = new List<Vector4>(vertices);
            baked.GetUVs(1, uv);
            var expansion = Vector3.zero;
            int cursor = 0;
            for (int v = 0; v < vertices; v++)
            {
                if (v == uv.Count) uv.Add(Vector4.zero);
                Vector4 range = uv[v];
                range.z = cursor;
                range.w = entries[v]?.Count ?? 0;
                uv[v] = range;
                expansion = Vector3.Max(expansion, travel[v]);
                if (entries[v] == null) continue;
                foreach (var e in entries[v])
                {
                    data[cursor++] = new Color(e.Position.x, e.Position.y, e.Position.z, e.Slot);
                    data[cursor++] = new Color(e.Normal.x, e.Normal.y, e.Normal.z);
                    data[cursor++] = new Color(e.Tangent.x, e.Tangent.y, e.Tangent.z);
                }
            }
            baked.SetUVs(1, uv);
            var bounds = baked.bounds;
            bounds.Expand(2f * expansion);
            baked.bounds = bounds;

            var shader = Shader.Find("Hidden/TSMP/BlendShape Weights");
            if (!shader) throw new InvalidOperationException("Cannot find the TSMP BlendShape weight shader.");
            var material = new Material(shader) { name = "Decode BlendShape Weights" };
            owned.Add(material);
            uint hash = StableHash.VariableHash(sender.GetType(), nameof(sender.blendShapeBytes), "blendshapes.packed");
            material.SetVector("_Field", new Vector4(channel, id, hash & 65535u, hash >> 16));
            material.SetTexture("_ShapeMap", Texture("BlendShape Mapping", slots.Count, 1, slots.ToArray(), owned));
            var weights = HumanoidSkinBaker.Palette("BlendShape Weights", slots.Count, material);
            owned.Add(weights);
            var deltas = Texture("BlendShape Deltas", Width, height, data, owned);
            foreach (var surface in materials)
            {
                surface.EnableKeyword("TSMP_BLEND_SHAPES");
                surface.SetTexture("_TSMPShapeWeights", weights);
                surface.SetTexture("_TSMPShapeDeltas", deltas);
            }
        }

        private static Texture2D Texture(string name, int width, int height, Color[] pixels, List<Object> owned)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            owned.Add(tex);
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
