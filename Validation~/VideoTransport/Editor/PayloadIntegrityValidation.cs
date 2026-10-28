#if !UDONSHARP
using System;
using System.IO;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Characterizes legacy integrity limits; no GPU or compressed-video claim.
public static class PayloadIntegrityValidation
{
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var target = new GameObject("Integrity receiver").AddComponent<PayloadIntegrityProbe>();
        var decoder = new GameObject("Integrity decoder").AddComponent<TSMPDecoder>();
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        string result = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        try
        {
            decoder.applyEveryFrame = false;
            decoder.debugLog = false;
            uint hash = StableHash.Fnv1A32(nameof(PayloadIntegrityProbe.receivedValue));
            decoder.bindingTargets = new Component[] { target };
            decoder.bindingNetworkIds = new ushort[] { 1 };
            decoder.bindingVariableHashes = new[] { hash };
            decoder.bindingFieldNames = new[] { nameof(PayloadIntegrityProbe.receivedValue) };
            decoder.bindingValueTypes = new[] { (byte)NetworkValueType.Int32 };
            decoder.bindingDirections = new int[1];
            var payload = new byte[29];
            int cursor = NetworkFrameWriter.BeginNetworkFrame(payload, 0, 1);
            int start = cursor;
            cursor = NetworkFrameWriter.BeginVariableState(payload, cursor, 1, 1);
            cursor = NetworkValueEntryWriter.WriteVariableValue(payload, cursor, hash, NetworkFrameProtocol.ValueTypeInt32, 42);
            Check(cursor == payload.Length && NetworkFrameWriter.EndVariableState(payload, start, cursor, 1)
                && NetworkFrameWriter.EndNetworkFrame(payload, 0, 1), "Fixture construction");
            var header = FrameHeader.CreateDefault();
            header.ActiveWidthBlocks = 160;
            header.ActiveHeightBlocks = 90;
            header.DecodeSampleSize = 4;
            header.FrameIndex = 1;
            header.PayloadSize = (ushort)payload.Length;
            var headerBytes = new byte[FrameHeader.Size];
            header.WriteTo(headerBytes, 0);
            Check(FrameHeader.TryRead(headerBytes, 0, out var original), "Original header validity");
            typeof(TSMPDecoder).GetField("_payloadBytes", Private).SetValue(decoder, payload);
            typeof(TSMPDecoder).GetField("_payloadDataBytes", Private).SetValue(decoder, payload.Length);
            var apply = typeof(TSMPDecoder).GetMethod("ApplyNetworkFrame", Private);
            Check((bool)apply.Invoke(decoder, null) && target.receivedValue == 42, "Original value application");
            // Keep routing, type, length and the entire header intact; mutate only the Int32 value.
            payload[25] ^= 1;
            Check(FrameHeader.TryRead(headerBytes, 0, out var unchanged) && unchanged.HeaderCrc32 == original.HeaderCrc32,
                "Payload mutation unexpectedly affected header CRC");
            Check((bool)apply.Invoke(decoder, null) && target.receivedValue == 43 && decoder.lastAppliedVariableCount == 1,
                "Legacy parser/apply contract changed; review integrity expectations");
            File.WriteAllText(result, "PASS\nCharacterization=legacy payload mutation is accepted\nOriginalValue=42\nMutatedAppliedValue=43\nHeaderCrcUnchanged=True\n" +
                "Scope=Native parser and field application invoked directly; no GPU/video/Udon/RPC test. This pass documents an integrity limit, not safe rejection.\n");
        }
        catch (Exception error) { File.WriteAllText(result, "FAIL\n" + error); throw; }
        finally { Object.DestroyImmediate(decoder.gameObject); Object.DestroyImmediate(target.gameObject); }
    }

    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
