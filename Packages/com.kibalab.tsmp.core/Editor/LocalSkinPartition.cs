using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace K13A.TSMP.Editor
{
    internal sealed class LocalSkinPartition
    {
        private readonly List<int>[] _triangles;
        private readonly Dictionary<int, int> _indices = new Dictionary<int, int>();
        internal readonly List<int> Vertices = new List<int>();

        private LocalSkinPartition(int subMeshes)
        {
            _triangles = new List<int>[subMeshes];
            for (int i = 0; i < subMeshes; i++) _triangles[i] = new List<int>();
        }

        internal static void Split(Mesh mesh, Transform hips, Transform[] bones, BoneWeight[] weights,
            Transform[] roots, out LocalSkinPartition gpu, out LocalSkinPartition local)
        {
            gpu = null;
            local = null;
            if (roots == null || roots.Length == 0) return;
            var keepOnRenderer = new bool[bones.Length];
            foreach (Transform root in roots)
            {
                if (!root || root == hips || !root.IsChildOf(hips))
                    throw new ArgumentException("Each bone kept on the skinned renderer must be below Hips on the receiving rig.");
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] && bones[i].IsChildOf(root)) keepOnRenderer[i] = true;
            }
            var touched = new bool[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight w = weights[i];
                touched[i] = (w.weight0 > 0 && keepOnRenderer[w.boneIndex0]) || (w.weight1 > 0 && keepOnRenderer[w.boneIndex1]) ||
                             (w.weight2 > 0 && keepOnRenderer[w.boneIndex2]) || (w.weight3 > 0 && keepOnRenderer[w.boneIndex3]);
            }
            if (Array.IndexOf(touched, true) < 0) return;
            gpu = new LocalSkinPartition(mesh.subMeshCount);
            local = new LocalSkinPartition(mesh.subMeshCount);
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles)
                    throw new ArgumentException("Partitioning a skin requires triangle submeshes.");
                int[] indices = mesh.GetTriangles(s);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var side = touched[indices[i]] || touched[indices[i + 1]] || touched[indices[i + 2]] ? local : gpu;
                    for (int j = i; j < i + 3; j++)
                    {
                        int v = indices[j];
                        if (!side._indices.TryGetValue(v, out int n))
                        {
                            n = side.Vertices.Count;
                            side.Vertices.Add(v);
                            side._indices.Add(v, n);
                        }
                        side._triangles[s].Add(n);
                    }
                }
            }
            if (gpu.Vertices.Count == 0)
                throw new ArgumentException("Every triangle is kept on the original skinned renderer; no GPU bake is needed.");
            if (local.Vertices.Count == 0) gpu = local = null;
        }

        internal Mesh BuildMesh(Mesh source, bool keepSkin, List<UnityEngine.Object> owned)
        {
            var mesh = new Mesh { name = source.name, indexFormat = source.indexFormat };
            owned.Add(mesh);
            mesh.SetVertexBufferParams(Vertices.Count, source.GetVertexAttributes());
            using (var data = Mesh.AcquireReadOnlyMeshData(source))
            {
                for (int s = 0; s < source.vertexBufferCount; s++)
                {
                    int stride = source.GetVertexBufferStride(s);
                    var src = data[0].GetVertexData<byte>(s);
                    using (var dst = new NativeArray<byte>(Vertices.Count * stride, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                    {
                        for (int i = 0; i < Vertices.Count; i++)
                            NativeArray<byte>.Copy(src, Vertices[i] * stride, dst, i * stride, stride);
                        mesh.SetVertexBufferData(dst, 0, 0, dst.Length, s);
                    }
                }
            }
            mesh.subMeshCount = _triangles.Length;
            for (int s = 0; s < _triangles.Length; s++) mesh.SetTriangles(_triangles[s], s, false);
            mesh.RecalculateBounds();
            if (!keepSkin) return mesh;

            mesh.name += " Skinned Renderer";
            mesh.bindposes = source.bindposes;
            mesh.boneWeights = Select(source.boneWeights);
            if (source.blendShapeCount == 0) return mesh;
            var positions = new Vector3[source.vertexCount];
            var normals = new Vector3[source.vertexCount];
            var tangents = new Vector3[source.vertexCount];
            for (int s = 0; s < source.blendShapeCount; s++)
            {
                string name = source.GetBlendShapeName(s);
                for (int f = 0; f < source.GetBlendShapeFrameCount(s); f++)
                {
                    source.GetBlendShapeFrameVertices(s, f, positions, normals, tangents);
                    mesh.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(s, f),
                        Select(positions), Select(normals), Select(tangents));
                }
            }
            return mesh;
        }

        internal T[] Select<T>(T[] source)
        {
            var result = new T[Vertices.Count];
            for (int i = 0; i < result.Length; i++) result[i] = source[Vertices[i]];
            return result;
        }
    }
}
