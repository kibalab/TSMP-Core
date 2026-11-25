using System;
using System.Collections.Generic;
using System.IO;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace K13A.TSMP.Editor
{
    public static class HumanoidSkinBaker
    {
        private const int HumanBones = (int)HumanBodyBones.LastBone;

        public static MeshRenderer Bake(Animator animator, SkinnedMeshRenderer source, int channel, int networkId,
            float motionMargin, string path)
            => Bake(animator, source, channel, networkId, motionMargin, path, null);

        public static MeshRenderer Bake(Animator animator, SkinnedMeshRenderer source, int channel, int networkId,
            float motionMargin, string path, Transform[] nativeBoneRoots)
        {
            if (Application.isPlaying || !animator || !source || !animator.gameObject.scene.IsValid() ||
                !source.transform.IsChildOf(animator.transform))
                throw new ArgumentException("Select a scene Animator and a skinned mesh in its hierarchy in Edit Mode.");
            if (!animator.avatar || !animator.avatar.isValid || !animator.isHuman)
                throw new ArgumentException("The Animator needs a valid Humanoid avatar.");
            if (!source.gameObject.scene.IsValid() || EditorUtility.IsPersistent(source))
                throw new ArgumentException("Choose a scene instance, not a model asset.");
            if (channel < 0 || channel > 3 || networkId < 0 || networkId > ushort.MaxValue ||
                !(motionMargin >= 0f) || float.IsInfinity(motionMargin))
                throw new ArgumentException("Invalid shader channel, sender network ID or motion margin.");
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || File.Exists(path) ||
                Array.IndexOf(path.Split('/'), "..") >= 0)
                throw new ArgumentException("Choose a new .asset file in Assets. Existing bakes are not overwritten.");

            Mesh sourceMesh = source.sharedMesh;
            if (!sourceMesh || !sourceMesh.isReadable)
                throw new ArgumentException("The source mesh must have Read/Write enabled for baking.");
            Transform[] bones = source.bones;
            Matrix4x4[] bind = sourceMesh.bindposes;
            var weights = sourceMesh.boneWeights;
            if (bones.Length == 0 || bind.Length != bones.Length || weights.Length != sourceMesh.vertexCount ||
                2 + 3 * bones.Length > SystemInfo.maxTextureSize)
                throw new ArgumentException("The mesh has no usable bind poses or exceeds the palette texture limit.");
            foreach (byte count in sourceMesh.GetBonesPerVertex())
                if (count > 4) throw new ArgumentException("Reduce the mesh to at most four influences per vertex before baking.");

            Transform root = animator.transform;
            for (var t = root; t; t = t.parent) RequireUniformScale(t);
            var human = new Dictionary<Transform, int>();
            for (int i = 0; i < HumanBones; i++)
            {
                var t = animator.GetBoneTransform((HumanBodyBones)i);
                if (t) human.Add(t, i);
            }
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (!hips || hips == root || !hips.IsChildOf(root))
                throw new ArgumentException("Hips must be a mapped bone below the Animator.");
            bool[] used = GetInfluences(weights, bones, hips, out var ids, out var influences);
            LocalSkinPartition.Split(sourceMesh, hips, bones, weights, nativeBoneRoots, out var gpu, out var local);
            if (gpu != null)
            {
                if (source.GetComponent<Cloth>())
                    throw new ArgumentException("A Cloth component requires the original mesh topology. Keep its renderer unbaked.");
                used = GetInfluences(gpu.Select(weights), bones, hips, out ids, out influences);
                var remap = new int[bones.Length];
                int count = 0;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (!used[i]) continue;
                    remap[i] = count;
                    bones[count] = bones[i];
                    bind[count++] = bind[i];
                }
                Array.Resize(ref bones, count);
                Array.Resize(ref bind, count);
                used = new bool[count];
                for (int i = 0; i < count; i++) used[i] = true;
                for (int i = 0; i < ids.Count; i++)
                {
                    Vector4 v = ids[i];
                    ids[i] = new Vector4(remap[(int)v.x], remap[(int)v.y], remap[(int)v.z], remap[(int)v.w]);
                }
            }

            Matrix4x4 meshToRig = root.worldToLocalMatrix * source.transform.localToWorldMatrix;
            if (!(meshToRig.determinant > 0f)) throw new ArgumentException("The mesh transform is mirrored or singular.");
            for (var t = hips.parent; t && t != root; t = t.parent) RequireUniformScale(t);
            Color[] texels = BuildRigTable(root, hips, human, bones, used, bind, meshToRig.inverse, out int width, out int height);

            Shader poseShader = FindShader("Hidden/TSMP/Humanoid Pose Palette");
            Shader skinShader = FindShader("Hidden/TSMP/Humanoid Skin Palette");
            Shader surface = FindShader("TSMP/Humanoid GPU Skinning");
            var owned = new List<Object>();
            bool created = false;
            bool originallyEnabled = source.enabled;
            Bounds originalBounds = source.localBounds;
            Material[] originalMaterials = source.sharedMaterials;
            GameObject proxy = null;
            try
            {
                var mesh = gpu == null ? Object.Instantiate(sourceMesh) : gpu.BuildMesh(sourceMesh, false, owned);
                if (gpu == null) owned.Add(mesh);
                Mesh localMesh = local?.BuildMesh(sourceMesh, true, owned);
                PrepareMesh(mesh, meshToRig, ids, influences, motionMargin);

                var rig = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                    { name = "Rig", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                owned.Add(rig);
                rig.SetPixels(texels);
                rig.Apply(false, false);
                var poseMaterial = new Material(poseShader) { name = "Decode Humanoid Pose" };
                owned.Add(poseMaterial);
                uint hash = StableHash.VariableHash(typeof(TSMPNetworkHumanoidPoseSync), "poseBytes", "humanoid.pose");
                poseMaterial.SetVector("_Field", new Vector4(channel, networkId, hash & 65535u, hash >> 16));
                var pose = Palette("Pose", HumanBones + 2, poseMaterial);
                owned.Add(pose);
                var skinMaterial = new Material(skinShader) { name = "Build Skin Matrices" };
                owned.Add(skinMaterial);
                skinMaterial.SetTexture("_Pose", pose);
                skinMaterial.SetTexture("_Rig", rig);
                var palette = Palette("Skin Matrices", 2 + 3 * bones.Length, skinMaterial);
                owned.Add(palette);
                Material[] materials = CreateMaterials(gpu != null ? gpu.SelectMaterials(originalMaterials) : originalMaterials,
                    mesh.subMeshCount, surface, palette, owned);

                AssetDatabase.CreateAsset(mesh, path);
                created = true;
                for (int i = 1; i < owned.Count; i++) AssetDatabase.AddObjectToAsset(owned[i], mesh);
                AssetDatabase.SaveAssets();
                proxy = new GameObject(source.name + " GPU Skin", typeof(MeshFilter), typeof(MeshRenderer));
                proxy.transform.SetParent(root, false);
                proxy.layer = source.gameObject.layer;
                proxy.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = proxy.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = source.shadowCastingMode;
                renderer.receiveShadows = source.receiveShadows;
                renderer.allowOcclusionWhenDynamic = false;
                Undo.RegisterCreatedObjectUndo(proxy, "Bake GPU Skin");
                Undo.RecordObject(source, "Bake GPU Skin");
                if (localMesh)
                {
                    source.sharedMesh = localMesh;
                    source.sharedMaterials = local.SelectMaterials(originalMaterials);
                    source.localBounds = originalBounds;
                }
                source.enabled = localMesh != null;
                PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                return renderer;
            }
            catch
            {
                if (proxy) Object.DestroyImmediate(proxy);
                source.sharedMesh = sourceMesh;
                source.sharedMaterials = originalMaterials;
                source.localBounds = originalBounds;
                source.enabled = originallyEnabled;
                if (created) AssetDatabase.DeleteAsset(path);
                foreach (Object item in owned)
                    if (item && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
                throw;
            }
        }

        private static bool[] GetInfluences(BoneWeight[] weights, Transform[] bones, Transform hips,
            out List<Vector4> ids, out List<Vector4> influences)
        {
            var used = new bool[bones.Length];
            ids = new List<Vector4>(weights.Length);
            influences = new List<Vector4>(weights.Length);
            foreach (BoneWeight w in weights)
            {
                var indices = new Vector4(w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3);
                var values = new Vector4(w.weight0, w.weight1, w.weight2, w.weight3);
                float total = 0f;
                for (int j = 0; j < 4; j++)
                {
                    if (values[j] == 0f)
                    {
                        indices[j] = 0;
                        continue;
                    }
                    if (!(values[j] > 0f) || float.IsInfinity(values[j]))
                        throw new ArgumentException("The mesh contains an invalid bone weight.");
                    int id = (int)indices[j];
                    if (id < 0 || id >= bones.Length || !bones[id] || !bones[id].IsChildOf(hips))
                        throw new ArgumentException("Every weighted bone must be Hips or one of its descendants.");
                    used[id] = true;
                    total += values[j];
                }
                if (!(total > 0f)) throw new ArgumentException("The mesh contains an unweighted vertex.");
                ids.Add(indices);
                influences.Add(values / total);
            }
            return used;
        }

        private static Color[] BuildRigTable(Transform root, Transform hips, Dictionary<Transform, int> human,
            Transform[] bones, bool[] used, Matrix4x4[] bind, Matrix4x4 rigToMesh, out int width, out int height)
        {
            var nodes = new List<Transform>();
            var nodeIds = new Dictionary<Transform, int>();
            var paths = new List<int>[bones.Length];
            int maxDepth = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                if (!used[i]) continue;
                var pathToBone = new List<int>();
                for (Transform t = bones[i]; t != hips; t = t.parent)
                {
                    if (!nodeIds.TryGetValue(t, out int id))
                    {
                        id = nodes.Count;
                        nodes.Add(t);
                        nodeIds.Add(t, id);
                    }
                    pathToBone.Add(1 + bones.Length + id);
                }
                pathToBone.Reverse();
                paths[i] = pathToBone;
                maxDepth = Mathf.Max(maxDepth, pathToBone.Count);
            }
            width = 4 + (maxDepth + 3) / 4;
            height = 1 + bones.Length + nodes.Count;
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
                throw new ArgumentException("The rig hierarchy exceeds the texture dimension limit.");
            var texels = new Color[width * height];
            Vector3 hipsScale = hips.localScale * (hips.parent.lossyScale.x / root.lossyScale.x);
            texels[0] = new Color(hipsScale.x, hipsScale.y, hipsScale.z, 0);
            Quaternion hipsRotation = Quaternion.Inverse(root.rotation) * hips.rotation;
            texels[1] = new Color(hipsRotation.x, hipsRotation.y, hipsRotation.z, hipsRotation.w);
            for (int i = 0; i < bones.Length; i++)
            {
                if (!used[i]) continue;
                int p = (1 + i) * width;
                Matrix4x4 bindFromRig = bind[i] * rigToMesh;
                for (int row = 0; row < 3; row++) texels[p + row] = bindFromRig.GetRow(row);
                texels[p + 3] = new Color(paths[i].Count, 0, 0, 0);
                for (int j = 0; j < paths[i].Count; j++) texels[p + 4 + j / 4][j % 4] = paths[i][j];
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                Transform t = nodes[i];
                int p = (1 + bones.Length + i) * width;
                Vector3 pos = t.localPosition, scale = t.localScale;
                Quaternion q = t.localRotation;
                texels[p] = new Color(pos.x, pos.y, pos.z, human.TryGetValue(t, out int id) ? id : -1);
                texels[p + 1] = new Color(q.x, q.y, q.z, q.w);
                texels[p + 2] = new Color(scale.x, scale.y, scale.z, 0);
            }
            return texels;
        }

        private static void PrepareMesh(Mesh mesh, Matrix4x4 meshToRig, List<Vector4> ids, List<Vector4> influences,
            float motionMargin)
        {
            mesh.name = mesh.name.Replace("(Clone)", string.Empty) + " GPU Skin";
            mesh.ClearBlendShapes();
            mesh.boneWeights = Array.Empty<BoneWeight>();
            mesh.bindposes = Array.Empty<Matrix4x4>();
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            Matrix4x4 normalMatrix = meshToRig.inverse.transpose;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = meshToRig.MultiplyPoint3x4(vertices[i]);
                if (i < normals.Length) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                if (i >= tangents.Length) continue;
                var dir = meshToRig.MultiplyVector(tangents[i]).normalized;
                tangents[i] = new Vector4(dir.x, dir.y, dir.z, tangents[i].w);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.SetUVs(2, ids);
            mesh.SetUVs(3, influences);
            mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            bounds.Expand(2f * motionMargin);
            mesh.bounds = bounds;
        }

        private static Material[] CreateMaterials(Material[] originals, int subMeshCount, Shader surface,
            CustomRenderTexture palette, List<Object> owned)
        {
            var materials = new Material[Mathf.Max(originals.Length, subMeshCount)];
            for (int i = 0; i < materials.Length; i++)
            {
                var original = i < originals.Length ? originals[i] : null;
                var mat = new Material(surface) { name = original ? original.name + " GPU Skin" : "GPU Skin" };
                owned.Add(mat);
                if (original && original.HasProperty("_MainTex"))
                {
                    mat.mainTexture = original.GetTexture("_MainTex");
                    mat.mainTextureScale = original.GetTextureScale("_MainTex");
                    mat.mainTextureOffset = original.GetTextureOffset("_MainTex");
                }
                if (original && original.HasProperty("_Color")) mat.color = original.GetColor("_Color");
                mat.SetTexture("_TSMPSkinPalette", palette);
                materials[i] = mat;
            }
            return materials;
        }

        private static Shader FindShader(string name) =>
            Shader.Find(name) ?? throw new InvalidOperationException("Missing shader: " + name);

        private static void RequireUniformScale(Transform t)
        {
            Vector3 s = t.localScale;
            if (!(s.x > 0f) || Mathf.Abs(s.x - s.y) > s.x * 0.0001f || Mathf.Abs(s.x - s.z) > s.x * 0.0001f)
                throw new ArgumentException("The Animator and ancestors above Hips require positive uniform scale: " + t.name + " " + s.ToString("G8"));
        }

        private static CustomRenderTexture Palette(string name, int width, Material material) =>
            new CustomRenderTexture(width, 1, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = name, material = material, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                updateMode = CustomRenderTextureUpdateMode.Realtime, initializationColor = Color.clear,
                initializationMode = CustomRenderTextureUpdateMode.OnLoad, doubleBuffered = false
            };
    }
}
