#if !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using K13A.TSMP.Udon;
using UnityEngine;
using Object = UnityEngine.Object;

// Recording-only stage. Receiver bones/cube are written only by TSMP; visual
// meshes follow those targets when RenderView is called.
public sealed class ShowcaseActors : IDisposable
{
    const int Layer = 8;
    readonly GameObject root;
    readonly bool sender;
    readonly List<Material> materials = new List<Material>();
    readonly List<Link> links = new List<Link>();
    readonly List<Joint> joints = new List<Joint>();
    readonly Avatar avatar;
    readonly Transform headVisual, visor;
    public readonly TSMPNetworkTransformSync transformSync;
    public readonly TSMPNetworkHumanoidPoseSync humanoidSync;
    public readonly RenderTexture view;
    public readonly Transform cube;
    public readonly Dictionary<HumanBodyBones, Transform> bones = new Dictionary<HumanBodyBones, Transform>();
    public readonly Camera camera;

    struct Link { public Transform a, b, mesh; public float radius; }
    struct Joint { public Transform bone, mesh; public float radius; }

    public ShowcaseActors(Shader shader, string role)
    {
        if (shader == null) throw new ArgumentNullException(nameof(shader));
        sender = string.Equals(role, "sender", StringComparison.OrdinalIgnoreCase);
        root = new GameObject("Showcase stage - " + role) { layer = Layer };
        try
        {
            var white = Material(shader, new Color(.83f, .9f, 1f));
            var blue = Material(shader, new Color(.18f, .58f, 1f), .15f);
            var purple = Material(shader, new Color(.55f, .37f, 1f), .12f);
            var dark = Material(shader, new Color(.025f, .065f, .12f));
            var grid = Material(shader, new Color(.055f, .115f, .18f), .15f);
            var floor = Primitive(PrimitiveType.Cube, "Stage", dark);
            floor.localPosition = new Vector3(.2f, -.06f, 0);
            floor.localScale = new Vector3(3.5f, .1f, 2.9f);
            for (int n = -6; n <= 6; n++)
            {
                var lineX = Primitive(PrimitiveType.Cube, "Floor grid X", grid);
                lineX.localPosition = new Vector3(n * .25f, -.004f, 0);
                lineX.localScale = new Vector3(.006f, .004f, 2.8f);
                var lineZ = Primitive(PrimitiveType.Cube, "Floor grid Z", grid);
                lineZ.localPosition = new Vector3(.2f, -.004f, n * .2f);
                lineZ.localScale = new Vector3(3.4f, .004f, .006f);
            }

            var rig = new GameObject("Humanoid demo rig") { layer = Layer };
            rig.transform.SetParent(root.transform, false);
            Bone(HumanBodyBones.Hips, rig.transform, new Vector3(0, 1, 0));
            Bone(HumanBodyBones.Spine, bones[HumanBodyBones.Hips], new Vector3(0, .2f, 0));
            Bone(HumanBodyBones.Chest, bones[HumanBodyBones.Spine], new Vector3(0, .2f, 0));
            Bone(HumanBodyBones.Neck, bones[HumanBodyBones.Chest], new Vector3(0, .2f, 0));
            Bone(HumanBodyBones.Head, bones[HumanBodyBones.Neck], new Vector3(0, .2f, 0));
            Bone(HumanBodyBones.LeftUpperArm, bones[HumanBodyBones.Chest], new Vector3(-.2f, .1f, 0));
            Bone(HumanBodyBones.LeftLowerArm, bones[HumanBodyBones.LeftUpperArm], new Vector3(-.3f, 0, 0));
            Bone(HumanBodyBones.LeftHand, bones[HumanBodyBones.LeftLowerArm], new Vector3(-.25f, 0, 0));
            Bone(HumanBodyBones.RightUpperArm, bones[HumanBodyBones.Chest], new Vector3(.2f, .1f, 0));
            Bone(HumanBodyBones.RightLowerArm, bones[HumanBodyBones.RightUpperArm], new Vector3(.3f, 0, 0));
            Bone(HumanBodyBones.RightHand, bones[HumanBodyBones.RightLowerArm], new Vector3(.25f, 0, 0));
            Bone(HumanBodyBones.LeftUpperLeg, bones[HumanBodyBones.Hips], new Vector3(-.1f, -.1f, 0));
            Bone(HumanBodyBones.LeftLowerLeg, bones[HumanBodyBones.LeftUpperLeg], new Vector3(0, -.4f, 0));
            Bone(HumanBodyBones.LeftFoot, bones[HumanBodyBones.LeftLowerLeg], new Vector3(0, -.4f, .05f));
            Bone(HumanBodyBones.RightUpperLeg, bones[HumanBodyBones.Hips], new Vector3(.1f, -.1f, 0));
            Bone(HumanBodyBones.RightLowerLeg, bones[HumanBodyBones.RightUpperLeg], new Vector3(0, -.4f, 0));
            Bone(HumanBodyBones.RightFoot, bones[HumanBodyBones.RightLowerLeg], new Vector3(0, -.4f, .05f));
            var human = new List<HumanBone>();
            foreach (var entry in bones)
                human.Add(new HumanBone { boneName = entry.Value.name, humanName = HumanTrait.BoneName[(int)entry.Key], limit = new HumanLimit { useDefaultValues = true } });
            var skeleton = new List<SkeletonBone>();
            foreach (Transform bone in rig.GetComponentsInChildren<Transform>())
                skeleton.Add(new SkeletonBone { name = bone.name, position = bone.localPosition, rotation = bone.localRotation, scale = bone.localScale });
            avatar = AvatarBuilder.BuildHumanAvatar(rig, new HumanDescription
            {
                human = human.ToArray(), skeleton = skeleton.ToArray(), armStretch = .05f, legStretch = .05f,
                upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f
            });
            if (avatar == null || !avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Showcase humanoid Avatar is invalid");
            var animator = rig.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = false;
            humanoidSync = rig.AddComponent<TSMPNetworkHumanoidPoseSync>();
            humanoidSync.networkId = 502;
            humanoidSync.animator = animator;
            humanoidSync.includeFingerBones = false;
            humanoidSync.receiveInterpolation = ReceiveInterpolationMode.Discrete;
            humanoidSync.ResolveBones();
            if (!animator.isHuman || humanoidSync.validBoneCount != 17) throw new InvalidOperationException("Showcase must resolve 17 humanoid bones");

            Connect(HumanBodyBones.Hips, HumanBodyBones.Spine, .12f, blue);
            Connect(HumanBodyBones.Spine, HumanBodyBones.Chest, .13f, blue);
            Connect(HumanBodyBones.Chest, HumanBodyBones.Neck, .08f, white);
            Connect(HumanBodyBones.Neck, HumanBodyBones.Head, .045f, white);
            Connect(HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm, .075f, purple);
            Connect(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, .055f, white);
            Connect(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, .045f, white);
            Connect(HumanBodyBones.Chest, HumanBodyBones.RightUpperArm, .075f, purple);
            Connect(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, .055f, white);
            Connect(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, .045f, white);
            Connect(HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, .075f, purple);
            Connect(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, .065f, white);
            Connect(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, .05f, white);
            Connect(HumanBodyBones.Hips, HumanBodyBones.RightUpperLeg, .075f, purple);
            Connect(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, .065f, white);
            Connect(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, .05f, white);
            foreach (var entry in bones)
            {
                if (entry.Key == HumanBodyBones.Head) continue;
                float radius = entry.Key == HumanBodyBones.Hips ? .12f : entry.Key == HumanBodyBones.Chest ? .13f : .063f;
                joints.Add(new Joint { bone = entry.Value, mesh = Primitive(PrimitiveType.Sphere, entry.Key + " joint", purple), radius = radius });
            }
            headVisual = Primitive(PrimitiveType.Sphere, "Head", white);
            headVisual.localScale = Vector3.one * .28f;
            visor = Primitive(PrimitiveType.Cube, "Visor", blue);
            visor.localScale = new Vector3(.19f, .055f, .04f);

            cube = Primitive(PrimitiveType.Cube, "Synced cube", blue);
            cube.localPosition = new Vector3(.9f, .45f, -.25f);
            cube.localScale = Vector3.one * .28f;
            transformSync = cube.gameObject.AddComponent<TSMPNetworkTransformSync>();
            transformSync.networkId = 501;
            transformSync.target = cube;
            transformSync.syncRigidbody = false;
            transformSync.compressionMode = CompressionMode.RotOnly;
            transformSync.receiveInterpolation = ReceiveInterpolationMode.Discrete;

            var key = new GameObject("Stage key") { layer = Layer };
            key.transform.SetParent(root.transform, false);
            key.transform.rotation = Quaternion.Euler(42, -28, 0);
            var keyLight = key.AddComponent<Light>();
            keyLight.type = LightType.Directional; keyLight.intensity = 1.2f; keyLight.color = new Color(.84f, .91f, 1f); keyLight.cullingMask = 1 << Layer;
            var fill = new GameObject("Stage fill") { layer = Layer };
            fill.transform.SetParent(root.transform, false);
            fill.transform.rotation = Quaternion.Euler(20, 155, 0);
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional; fillLight.intensity = .55f; fillLight.color = new Color(.5f, .48f, 1f); fillLight.cullingMask = 1 << Layer;
            var lens = new GameObject("Stage camera") { layer = Layer };
            lens.transform.SetParent(root.transform, false);
            camera = lens.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.47f;
            camera.transform.position = new Vector3(3.45f, 2.3f, -6f);
            camera.transform.LookAt(new Vector3(.25f, .97f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(11f / 255f, 21f / 255f, 35f / 255f);
            camera.cullingMask = 1 << Layer; camera.nearClipPlane = .1f; camera.farClipPlane = 30f;
            camera.allowHDR = false; camera.enabled = false;
            view = new RenderTexture(480, 480, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = role + " stage", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!view.Create()) throw new InvalidOperationException("Showcase view allocation failed");
            camera.targetTexture = view;
            UpdateMeshes();
        }
        catch { Dispose(); throw; }
    }

    public void Animate(float seconds)
    {
        if (!sender) throw new InvalidOperationException("Receiver stage must be animated only by decoded TSMP data");
        foreach (Transform bone in bones.Values) bone.localRotation = Quaternion.identity;
        bones[HumanBodyBones.Hips].localPosition = new Vector3(.25f * Mathf.Sin(seconds), 1f + .04f * Mathf.Sin(2f * seconds), .12f * Mathf.Cos(seconds));
        bones[HumanBodyBones.Hips].localRotation = Quaternion.Euler(0, 8f * Mathf.Sin(seconds * .7f), 0);
        bones[HumanBodyBones.Chest].localRotation = Quaternion.Euler(0, 8f * Mathf.Sin(seconds * 1.3f), 0);
        bones[HumanBodyBones.LeftUpperArm].localRotation = Quaternion.Euler(0, 0, -28f - 12f * Mathf.Sin(seconds * 1.6f));
        bones[HumanBodyBones.LeftLowerArm].localRotation = Quaternion.Euler(0, 0, -65f + 25f * Mathf.Sin(seconds * 3.5f));
        bones[HumanBodyBones.LeftHand].localRotation = Quaternion.Euler(18f * Mathf.Sin(seconds * 4f), 0, 0);
        bones[HumanBodyBones.RightUpperArm].localRotation = Quaternion.Euler(0, 0, -67f + 4f * Mathf.Sin(seconds * 1.6f));
        bones[HumanBodyBones.RightLowerArm].localRotation = Quaternion.Euler(0, 0, 18f + 6f * Mathf.Sin(seconds * 1.3f));
        bones[HumanBodyBones.LeftUpperLeg].localRotation = Quaternion.Euler(2f * Mathf.Sin(seconds), 0, 2f * Mathf.Sin(seconds * 1.6f));
        bones[HumanBodyBones.RightUpperLeg].localRotation = Quaternion.Euler(-2f * Mathf.Sin(seconds), 0, -2f * Mathf.Sin(seconds * 1.6f));
        cube.localPosition = new Vector3(.92f + .27f * Mathf.Sin(seconds * 1.2f), .48f + .17f * Mathf.Sin(seconds * 1.7f), -.3f + .25f * Mathf.Cos(seconds));
        cube.localRotation = Quaternion.Euler(seconds * 38f, seconds * 55f, seconds * 23f);
        cube.localScale = Vector3.one * (.28f + .035f * Mathf.Sin(seconds * 2.2f));
    }

    public void RenderView() { UpdateMeshes(); camera.Render(); }

    void UpdateMeshes()
    {
        foreach (Link link in links)
        {
            Vector3 delta = link.b.position - link.a.position;
            link.mesh.position = (link.a.position + link.b.position) * .5f;
            link.mesh.rotation = delta.sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity;
            link.mesh.localScale = new Vector3(link.radius * 2f, delta.magnitude * .5f, link.radius * 2f);
        }
        foreach (Joint joint in joints)
        {
            joint.mesh.position = joint.bone.position;
            joint.mesh.localScale = Vector3.one * (joint.radius * 2f);
        }
        Transform head = bones[HumanBodyBones.Head];
        headVisual.position = head.position + head.rotation * new Vector3(0, .04f, 0);
        headVisual.rotation = head.rotation;
        visor.position = head.position + head.rotation * new Vector3(0, .04f, -.13f);
        visor.rotation = head.rotation;
    }

    void Bone(HumanBodyBones id, Transform parent, Vector3 localPosition)
    {
        var value = new GameObject(id.ToString()) { layer = Layer }.transform;
        value.SetParent(parent, false); value.localPosition = localPosition; bones.Add(id, value);
    }
    void Connect(HumanBodyBones a, HumanBodyBones b, float radius, Material material)
    {
        links.Add(new Link { a = bones[a], b = bones[b], radius = radius, mesh = Primitive(PrimitiveType.Capsule, a + " segment", material) });
    }
    Transform Primitive(PrimitiveType type, string name, Material material)
    {
        var value = GameObject.CreatePrimitive(type); value.name = name; value.layer = Layer;
        value.transform.SetParent(root.transform, false);
        value.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = value.GetComponent<Collider>(); if (collider != null) Object.Destroy(collider);
        return value.transform;
    }
    Material Material(Shader shader, Color color, float emission = 0)
    {
        var value = new Material(shader) { color = color }; materials.Add(value);
        if (value.HasProperty("_Metallic")) value.SetFloat("_Metallic", 0);
        if (value.HasProperty("_Glossiness")) value.SetFloat("_Glossiness", .22f);
        if (emission > 0 && value.HasProperty("_EmissionColor")) { value.EnableKeyword("_EMISSION"); value.SetColor("_EmissionColor", color * emission); }
        return value;
    }
    public void Dispose()
    {
        if (camera != null) camera.targetTexture = null;
        if (view != null) { view.Release(); Object.Destroy(view); }
        if (root != null) Object.Destroy(root);
        if (avatar != null) Object.Destroy(avatar);
        foreach (Material material in materials) if (material != null) Object.Destroy(material);
        materials.Clear();
    }
}
#endif
