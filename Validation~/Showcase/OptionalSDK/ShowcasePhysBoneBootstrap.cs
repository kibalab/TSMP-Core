using UnityEngine;
using VRC.Dynamics;

// Private recording fixture. Uses the installed VRChat SDK solver and its normal
// player loop. Do not call the scheduler manually: the SDK already registers it.
public static class ShowcasePhysBoneBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        if (ContactManager.Inst == null)
        {
            var contacts = new GameObject("Showcase SDK Contact Manager");
            Object.DontDestroyOnLoad(contacts);
            ContactManager.Inst = contacts.AddComponent<ContactManager>();
        }
        if (PhysBoneManager.Inst == null)
        {
            var physics = new GameObject("Showcase SDK PhysBone Manager");
            Object.DontDestroyOnLoad(physics);
            PhysBoneManager.Inst = physics.AddComponent<PhysBoneManager>();
            PhysBoneManager.Inst.IsSDK = true;
            PhysBoneManager.Inst.Init();
        }
        VRCDynamicsScheduler.OnFrameComplete += CountCompletedFrames;
        var receipt = new GameObject("Showcase PhysBone receipt");
        Object.DontDestroyOnLoad(receipt);
        receipt.AddComponent<ShowcasePhysBoneReceipt>();
    }

    public static int CompletedFrames { get; private set; }
    static void CountCompletedFrames() { CompletedFrames++; }
}

public class ShowcasePhysBoneReceipt : MonoBehaviour
{
    [System.Serializable] class Receipt
    {
        public string solver = "Installed official VRChat SDK, automatic player loop";
        public string role;
        public int completedFrames, physBones, registeredPhysBones, skirtTransforms;
        public float maximumSkirtLocalRotationDegrees;
        public bool finite = true;
    }
    readonly Receipt receipt = new Receipt();
    Transform[] skirt;
    VRCPhysBoneBase[] physics;
    Quaternion[] initial;
    string output;
    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--role") receipt.role = args[i + 1];
            if (args[i] == "--capture-root") output = args[i + 1];
        }
        var transforms = new System.Collections.Generic.List<Transform>();
        foreach (var t in Object.FindObjectsOfType<Transform>())
            if (t.name.Contains("Skirt") && !t.name.EndsWith("Root")) transforms.Add(t);
        skirt = transforms.ToArray(); initial = new Quaternion[skirt.Length];
        receipt.skirtTransforms = skirt.Length;
        for (int i = 0; i < skirt.Length; i++) initial[i] = skirt[i].localRotation;
        physics = Object.FindObjectsOfType<VRCPhysBoneBase>(); receipt.physBones = physics.Length;
        foreach (var pb in physics) if (PhysBoneManager.Inst.HasPhysBone(pb)) receipt.registeredPhysBones++;
        VRCDynamicsScheduler.OnFrameComplete += Sample;
    }
    void Sample()
    {
        receipt.completedFrames++;
        int registered = 0;
        foreach (var pb in physics) if (PhysBoneManager.Inst.HasPhysBone(pb)) registered++;
        receipt.registeredPhysBones = Mathf.Max(receipt.registeredPhysBones, registered);
        for (int i = 0; i < skirt.Length; i++)
        {
            var q = skirt[i].localRotation;
            var angle = Quaternion.Angle(initial[i], q);
            receipt.finite &= !float.IsNaN(angle) && !float.IsInfinity(angle);
            receipt.maximumSkirtLocalRotationDegrees = Mathf.Max(receipt.maximumSkirtLocalRotationDegrees, angle);
        }
    }
    void OnApplicationQuit()
    {
        VRCDynamicsScheduler.OnFrameComplete -= Sample;
        if (!string.IsNullOrEmpty(output))
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, receipt.role + "-physbone.json"), JsonUtility.ToJson(receipt, true));
    }
}
