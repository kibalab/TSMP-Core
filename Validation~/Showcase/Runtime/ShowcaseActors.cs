#if !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

// Recording-only stage. Receiver bones/cubes are written only by TSMP; visual
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
    readonly List<Mesh> importedMeshes = new List<Mesh>();
    readonly bool imported;
    readonly Animator importedAnimator;
    PlayableGraph danceGraph;
    AnimationClipPlayable dancePlayable;
    readonly Transform headVisual, visor;
    public const int CubeCount = 12;
    public readonly TSMPNetworkTransformSync[] transformSyncs = new TSMPNetworkTransformSync[CubeCount];
    public readonly TSMPNetworkHumanoidPoseSync humanoidSync;
    public readonly RenderTexture view;
    public readonly Transform[] cubes = new Transform[CubeCount];
    readonly Rigidbody[] cubeBodies = new Rigidbody[CubeCount];
    readonly PhysicMaterial rainPhysicsMaterial;
    int releaseOrdinal;
    float nextReleaseTime;
    float lastRainSeconds = -1;
    public int FloorCollisionCount { get; private set; }
    public int CubeCollisionCount { get; private set; }
    public int RespawnCount { get; private set; }
    public int ReleasedCubeCount { get; private set; }
    public float MaximumDownwardSpeed { get; private set; }
    public int DynamicCubeCount
    {
        get
        {
            int count = 0;
            foreach (var body in cubeBodies) if (body != null && !body.isKinematic) count++;
            return count;
        }
    }
    public readonly Dictionary<HumanBodyBones, Transform> bones = new Dictionary<HumanBodyBones, Transform>();
    public readonly Camera camera;

    struct Link { public Transform a, b, mesh; public float radius; }
    struct Joint { public Transform bone, mesh; public float radius; }

    public ShowcaseActors(Shader shader, string role, GameObject avatarPrefab = null, AnimationClip danceClip = null)
    {
        if (shader == null) throw new ArgumentNullException(nameof(shader));
        sender = string.Equals(role, "sender", StringComparison.OrdinalIgnoreCase);
        imported = avatarPrefab != null;
        if (imported && (danceClip == null || !danceClip.humanMotion)) throw new InvalidOperationException("Imported showcase avatar needs a Humanoid dance clip");
        root = new GameObject("Showcase stage - " + role) { layer = Layer };
        try
        {
            var white = Material(shader, new Color(.83f, .9f, 1f));
            var blue = Material(shader, new Color(.18f, .58f, 1f), .15f);
            var purple = Material(shader, new Color(.55f, .37f, 1f), .12f);
            var teal = Material(shader, new Color(.06f, .62f, .57f));
            var dark = Material(shader, new Color(.72f, .77f, .82f));
            var grid = Material(shader, new Color(.62f, .67f, .73f));
            var floor = Primitive(PrimitiveType.Cube, "Stage", dark, true);
            floor.localPosition = new Vector3(.2f, -.06f, 0);
            floor.localScale = new Vector3(3.5f, .1f, 2.9f);
            rainPhysicsMaterial = new PhysicMaterial("Showcase rain contact")
            {
                dynamicFriction = .5f, staticFriction = .65f, bounciness = .28f,
                frictionCombine = PhysicMaterialCombine.Average, bounceCombine = PhysicMaterialCombine.Maximum
            };
            floor.GetComponent<Collider>().sharedMaterial = rainPhysicsMaterial;
            floor.GetComponent<Collider>().enabled = sender;
            for (int n = -6; n <= 6; n++)
            {
                var lineX = Primitive(PrimitiveType.Cube, "Floor grid X", grid);
                lineX.localPosition = new Vector3(n * .25f, -.004f, 0);
                lineX.localScale = new Vector3(.006f, .004f, 2.8f);
                var lineZ = Primitive(PrimitiveType.Cube, "Floor grid Z", grid);
                lineZ.localPosition = new Vector3(.2f, -.004f, n * .2f);
                lineZ.localScale = new Vector3(3.4f, .004f, .006f);
            }

            if (!imported)
            {
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
            }
            else
            {
                var rig = Object.Instantiate(avatarPrefab, root.transform, false);
                rig.name = role + " private Humanoid";
                foreach (Transform part in rig.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = Layer;
                importedAnimator = rig.GetComponent<Animator>();
                if (importedAnimator == null || importedAnimator.avatar == null || !importedAnimator.avatar.isValid || !importedAnimator.isHuman)
                    throw new InvalidOperationException("Imported showcase avatar is not a valid Humanoid");
                importedAnimator.runtimeAnimatorController = null;
                importedAnimator.applyRootMotion = false;
                importedAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                for (int id = 0; id < (int)HumanBodyBones.LastBone; id++)
                {
                    var bone = importedAnimator.GetBoneTransform((HumanBodyBones)id);
                    if (bone != null) bones.Add((HumanBodyBones)id, bone);
                }
                foreach (var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.updateWhenOffscreen = true;
                    var mesh = renderer.sharedMesh;
                    if (mesh == null) continue;
                    var a = mesh.uv2; var b = mesh.uv3;
                    if (a.Length != mesh.vertexCount || b.Length != mesh.vertexCount) continue;
                    var copy = Object.Instantiate(mesh); importedMeshes.Add(copy);
                    var tangents = new Vector4[copy.vertexCount];
                    for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(a[i].x, a[i].y, b[i].x, b[i].y);
                    copy.SetTangents(tangents); renderer.sharedMesh = copy;
                }
                humanoidSync = rig.AddComponent<TSMPNetworkHumanoidPoseSync>();
                humanoidSync.networkId = 502; humanoidSync.animator = importedAnimator;
                humanoidSync.includeFingerBones = true;
                humanoidSync.receiveInterpolation = ReceiveInterpolationMode.Discrete;
                humanoidSync.ResolveBones();
                if (humanoidSync.validBoneCount < 17) throw new InvalidOperationException("Imported showcase humanoid bones unresolved");
                if (sender)
                {
                    importedAnimator.Rebind(); importedAnimator.Update(0);
                    danceGraph = PlayableGraph.Create("Sender-only Humanoid dance");
                    danceGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    dancePlayable = AnimationClipPlayable.Create(danceGraph, danceClip);
                    dancePlayable.SetApplyFootIK(false); dancePlayable.SetApplyPlayableIK(false);
                    var animationOutput = AnimationPlayableOutput.Create(danceGraph, "Sender dance", importedAnimator);
                    animationOutput.SetSourcePlayable(dancePlayable); danceGraph.Play(); danceGraph.Evaluate(0);
                }
                else importedAnimator.enabled = false;
            }

            for (int i = 0; i < CubeCount; i++)
            {
                var cube = Primitive(PrimitiveType.Cube, "Rain cube " + i, i % 3 == 0 ? purple : i % 3 == 1 ? blue : teal, true);
                cubes[i] = cube;
                cube.localScale = Vector3.one * (.22f + .02f * (i % 3));
                var collider = cube.GetComponent<Collider>();
                collider.sharedMaterial = rainPhysicsMaterial;
                collider.enabled = sender;
                var body = cube.gameObject.AddComponent<Rigidbody>();
                cubeBodies[i] = body;
                body.mass = .8f;
                body.drag = .025f;
                body.angularDrag = .08f;
                body.isKinematic = true;
                body.useGravity = false;
                body.detectCollisions = sender;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                ParkCube(i);
                if (sender)
                {
                    var probe = cube.gameObject.AddComponent<ShowcaseRainCollisionProbe>();
                    probe.owner = this; probe.index = i;
                }
                var sync = cube.gameObject.AddComponent<TSMPNetworkTransformSync>();
                transformSyncs[i] = sync;
                sync.networkId = (ushort)(600 + i);
                sync.target = cube;
                sync.targetRigidbody = body;
                sync.useLocalSpace = true;
                sync.syncRigidbody = false;
                sync.compressionMode = CompressionMode.RotOnly;
                sync.receiveInterpolation = ReceiveInterpolationMode.Discrete;
            }

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
            if (imported)
            {
                camera.orthographicSize = 1.52f;
                camera.transform.position = new Vector3(2.1f, 1.62f, 4f);
                camera.transform.LookAt(new Vector3(.65f, 1.23f, 0));
                key.transform.rotation = Quaternion.Euler(35, -135, 0);
                keyLight.color = Color.white;
                keyLight.intensity = .8f;
                fillLight.color = new Color(.8f, .85f, 1f);
                fillLight.intensity = .25f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.5f, .5f, .5f);
            }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.84f, .88f, .92f);
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
        if (imported)
        {
            dancePlayable.SetTime(seconds); danceGraph.Evaluate(0);
        }
        else
        {
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
        }
        AdvanceRain(seconds);
    }

    void AdvanceRain(float seconds)
    {
        if (seconds <= 0)
        {
            // The recording clock stays at zero during shader/transport warmup.
            // Keep the real physics bodies parked until that clock starts.
            if (lastRainSeconds > 0)
            {
                for (int i = 0; i < CubeCount; i++) ParkCube(i);
                releaseOrdinal = 0; nextReleaseTime = 0;
                FloorCollisionCount = CubeCollisionCount = RespawnCount = ReleasedCubeCount = 0;
                MaximumDownwardSpeed = 0;
            }
            lastRainSeconds = seconds;
            return;
        }
        while (seconds >= nextReleaseTime)
        {
            int index = releaseOrdinal % CubeCount;
            int cycle = releaseOrdinal / CubeCount;
            var body = cubeBodies[index];
            body.isKinematic = false; body.useGravity = true;
            // Recycle only at release. Every trajectory between releases comes
            // from Unity's gravity, contact response and cube-to-cube collisions.
            body.position = root.transform.TransformPoint(new Vector3(
                .67f + .27f * (releaseOrdinal % 4),
                2.43f + .10f * (releaseOrdinal % 3),
                -.23f + .23f * ((releaseOrdinal / 4) % 3)));
            body.rotation = root.transform.rotation * Quaternion.Euler(13 + index * 17, index * 31 + cycle * 13, 9 + index * 23);
            body.velocity = new Vector3((index % 2 == 0 ? 1 : -1) * .045f, -.15f, ((index % 3) - 1) * .035f);
            body.angularVelocity = new Vector3(1.1f + index * .13f, .7f - index * .09f, 1.4f - index * .11f);
            body.WakeUp();
            if (cycle > 0) RespawnCount++;
            ReleasedCubeCount++; releaseOrdinal++;
            nextReleaseTime += .23f;
        }
        foreach (var body in cubeBodies)
            if (!body.isKinematic) MaximumDownwardSpeed = Mathf.Max(MaximumDownwardSpeed, -body.velocity.y);
        lastRainSeconds = seconds;
    }

    void ParkCube(int index)
    {
        var body = cubeBodies[index];
        body.isKinematic = true; body.useGravity = false;
        body.position = root.transform.TransformPoint(new Vector3(.8f, 4.0f + index * .4f, 0));
        body.rotation = root.transform.rotation;
    }

    public void RecordRainCollision(int index, Collision collision)
    {
        if (!sender) throw new InvalidOperationException("Receiver must not simulate rain contacts");
        if (collision.collider.gameObject.name == "Stage") FloorCollisionCount++;
        var other = collision.collider.GetComponent<ShowcaseRainCollisionProbe>();
        if (other != null && index < other.index) CubeCollisionCount++;
    }

    public void RenderView() { UpdateMeshes(); camera.Render(); }

    void UpdateMeshes()
    {
        if (imported) return;
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

    // Check decoded pose application without evaluating the dance on the receiver.
    // Non-Humanoid clothing and hair remain the local PhysBone solver's responsibility.
    public void VerifyImportedPose()
    {
        if (!imported) return;
        var data = humanoidSync.poseBytes;
        if (data == null || data.Length < 21 || data[0] != 4 || (data[1] & 4) == 0)
            throw new InvalidOperationException("Imported showcase expected version4 animator-space pose");
        int count = Binary.ReadUInt16LE(data, 2);
        if (data.Length != 4 + count * 6 + 17 || count != humanoidSync.validBoneCount)
            throw new InvalidOperationException("Imported showcase pose length/bone count differs");
        int cursor = 4;
        for (int i = 0; i < count; i++)
        {
            var id = (HumanBodyBones)data[cursor++];
            var rotation = Binary.ReadPackedQuaternion12LE(data, cursor); cursor += 5;
            if (!bones.TryGetValue(id, out var bone) || Quaternion.Angle(bone.rotation, importedAnimator.transform.rotation * rotation) > .6f)
                throw new InvalidOperationException("Decoded imported humanoid rotation differs: " + id);
        }
        var position = Binary.ReadVector3Float32LE(data, cursor);
        var expected = importedAnimator.transform.position + importedAnimator.transform.rotation * position;
        if (Vector3.Distance(bones[HumanBodyBones.Hips].position, expected) > .006f)
            throw new InvalidOperationException("Decoded imported humanoid root differs");
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
    Transform Primitive(PrimitiveType type, string name, Material material, bool keepCollider = false)
    {
        var value = GameObject.CreatePrimitive(type); value.name = name; value.layer = Layer;
        value.transform.SetParent(root.transform, false);
        value.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = value.GetComponent<Collider>(); if (collider != null && !keepCollider) Object.Destroy(collider);
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
        if (danceGraph.IsValid()) danceGraph.Destroy();
        if (camera != null) camera.targetTexture = null;
        if (view != null) { view.Release(); Object.Destroy(view); }
        if (root != null) Object.Destroy(root);
        if (avatar != null) Object.Destroy(avatar);
        if (rainPhysicsMaterial != null) Object.Destroy(rainPhysicsMaterial);
        foreach (var mesh in importedMeshes) if (mesh != null) Object.Destroy(mesh);
        foreach (Material material in materials) if (material != null) Object.Destroy(material);
        materials.Clear();
    }
}

public sealed class ShowcaseRainCollisionProbe : MonoBehaviour
{
    public ShowcaseActors owner;
    public int index;
    void OnCollisionEnter(Collision collision) { owner.RecordRainCollision(index, collision); }
}
#endif
