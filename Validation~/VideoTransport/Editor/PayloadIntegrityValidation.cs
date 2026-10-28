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
            ValidateProtection(decoder, target, payload, headerBytes);
            File.WriteAllText(result, "PASS\nCharacterization=legacy payload mutation is accepted\nOriginalValue=42\nMutatedAppliedValue=43\nHeaderCrcUnchanged=True\n" +
                "PASS protected protocol CRC vector, exact valid length, dirty tails, empty CRC zero, strict legacy rejection and CompletePayload mutation/retry boundary\n" +
                "Scope=Direct Native legacy parser characterization and protected CompletePayload boundary; no GPU/video/Udon/RPC test. Legacy acceptance is an integrity limit; protected mutation must be rejected.\n");
        }
        catch (Exception error) { File.WriteAllText(result, "FAIL\n" + error); throw; }
        finally { Object.DestroyImmediate(decoder.gameObject); Object.DestroyImmediate(target.gameObject); }
    }

    static void ValidateProtection(TSMPDecoder decoder, PayloadIntegrityProbe target, byte[] payload, byte[] legacyHeader)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        uint[] table = Crc32Runtime.EnsureTable(null);
        Check(Crc32Runtime.Compute(System.Text.Encoding.ASCII.GetBytes("123456789"), 0, 9, table) == 0xCBF43926u, "CRC32 known vector");
        payload[25] ^= 1; // Restore the sender's original 42 before checksumming.
        var header = (byte[])legacyHeader.Clone();
        Check(FrameHeaderWriter.TryWritePayloadCrc32(header, 0, payload, payload.Length, table), "Protected header write");
        Check(FrameHeader.TryRead(header, 0, out var parsed) && (parsed.Flags & FrameFlags.PayloadCrc32) != 0
            && parsed.VersionMinor >= ProtocolConstants.PayloadCrcVersionMinor, "Protected header read/version/flag");
        Check(FrameHeaderReader.TryValidatePayload(header, 0, payload, payload.Length, true, table,
            out bool present, out uint expected, out uint actual, out string error) && present
            && expected == Crc32Runtime.Compute(payload, 0, payload.Length, table) && actual == expected, "Payload CRC roundtrip: " + error);
        var oversized = new byte[payload.Length + 32];
        Array.Copy(payload, oversized, payload.Length);
        for (int i = payload.Length; i < oversized.Length; i++) oversized[i] = 0xA5;
        Check(FrameHeaderReader.TryValidatePayload(header, 0, oversized, payload.Length, true, table, out present, out expected, out actual, out error), "Dirty buffer tail affected CRC");
        Check(!FrameHeaderReader.TryValidatePayload(header, 0, oversized, payload.Length + 1, true, table, out present, out expected, out actual, out error), "Header/data length mismatch accepted");
        Check(FrameHeaderReader.TryValidatePayload(legacyHeader, 0, payload, payload.Length, false, table, out present, out expected, out actual, out error)
            && !present, "Legacy header rejected in compatible mode");
        Check(FrameHeaderReader.TryValidatePayload(legacyHeader, 0, payload, payload.Length - 1, false, table, out present, out expected, out actual, out error),
            "Legacy explicit payload length override lost compatibility");
        Check(!FrameHeaderReader.TryValidatePayload(legacyHeader, 0, payload, payload.Length, true, table, out present, out expected, out actual, out error), "Strict mode admitted legacy header");
        var emptyHeader = new byte[FrameHeader.Size];
        var empty = FrameHeader.CreateDefault();
        empty.WriteTo(emptyHeader, 0);
        Check(FrameHeaderWriter.TryWritePayloadCrc32(emptyHeader, 0, new byte[0], 0, table)
            && FrameHeaderReader.TryValidatePayload(emptyHeader, 0, new byte[0], 0, true, table, out present, out expected, out actual, out error)
            && present && expected == 0 && actual == 0, "Empty payload CRC zero was treated as absent");
        Check(!FrameHeaderReader.TryValidatePayload(emptyHeader, 0, null, 0, true, table, out present, out expected, out actual, out error), "Null payload admitted");

        typeof(TSMPDecoder).GetField("_headerBytes", Private).SetValue(decoder, header);
        typeof(TSMPDecoder).GetField("_requestRequirePayloadCrc32", Private).SetValue(decoder, true);
        typeof(TSMPDecoder).GetField("_requestSafetyMode", Private).SetValue(decoder, 0);
        var complete = typeof(TSMPDecoder).GetMethod("CompletePayload", Private);
        decoder.lastFrameIndex = 100;
        target.receivedValue = -1;
        payload[25] ^= 1;
        decoder.lastFrameValid = false;
        complete.Invoke(decoder, null);
        Check(!decoder.lastFrameValid && decoder.lastPayloadCrcPresent && !decoder.lastPayloadCrcVerified
            && target.receivedValue == -1, "Protected mutation reached state application");
        payload[25] ^= 1;
        complete.Invoke(decoder, null);
        Check(decoder.lastFrameValid && decoder.lastPayloadCrcVerified && target.receivedValue == 42, "Protected retry did not apply original value");
    }

    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
