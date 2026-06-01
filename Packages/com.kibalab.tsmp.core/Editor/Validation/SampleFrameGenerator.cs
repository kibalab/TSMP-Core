using System.IO;
using K13A.TSMP;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    public static class SampleFrameGenerator
    {
        private const string OutputPath = "Assets/TSMP/Generated/tsmp_sample_640x360.png";

        [MenuItem("TSMP/Debug/Generate Sample 640x360 Frame")]
        public static void GenerateSampleFrame()
        {
            GenerateSampleFrameInternal(false);
        }

        [MenuItem("TSMP/Debug/Generate And Validate Sample 640x360 Frame")]
        public static void GenerateAndValidateSampleFrame()
        {
            GenerateSampleFrameInternal(true);
        }

        public static void GenerateAndValidateSampleFrameBatch()
        {
            bool success = GenerateSampleFrameInternal(true);
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static bool GenerateSampleFrameInternal(bool validate)
        {
            const int width = 640;
            const int height = 360;

            byte[] payloadBuffer = new byte[256];
            int payloadBytes = BuildNetworkSamplePayload(payloadBuffer);
            if (payloadBytes <= 0)
            {
                Debug.LogError("[TSMP] sample frame payload generation failed.");
                return false;
            }

            byte[] payload = new byte[payloadBytes];
            System.Buffer.BlockCopy(payloadBuffer, 0, payload, 0, payloadBytes);

            byte[] headerBytes = new byte[FrameHeader.Size];
            var header = FrameHeader.CreateDefault();
            header.Profile = 0;
            header.Flags = FrameFlags.None;
            header.BlockSize = ProtocolConstants.DefaultBlockSize;
            header.ActiveWidthBlocks = (ushort)FrameCapacity.GetActiveWidthBlocks(width, ProtocolConstants.DefaultBlockSize);
            header.ActiveHeightBlocks = (ushort)FrameCapacity.GetActiveHeightBlocks(height, ProtocolConstants.DefaultBlockSize);
            header.LayoutId = 0;
            header.StreamId = 1;
            header.FrameIndex = 1;
            header.TimestampMs = 0;
            header.PayloadType = PayloadType.NetworkFrame;
            header.PayloadSize = (ushort)payload.Length;
            header.WriteTo(headerBytes, 0);

            Texture2D texture = Luma4Raster.CreateTexture(width, height);
            if (!Luma4Raster.TryWriteFrame(
                    texture,
                    ProtocolConstants.DefaultBlockSize,
                    headerBytes,
                    payload,
                    out string error))
            {
                Object.DestroyImmediate(texture);
                Debug.LogError($"[TSMP] sample frame generation failed: {error}");
                return false;
            }

            string directory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(OutputPath, texture.EncodeToPNG());

            AssetDatabase.ImportAsset(OutputPath);
            Debug.Log($"[TSMP] Generated sample frame: {OutputPath}");

            if (validate && !ValidateGeneratedTexture(texture, payload.Length))
            {
                Object.DestroyImmediate(texture);
                return false;
            }

            Object.DestroyImmediate(texture);
            return true;
        }

        private static bool ValidateGeneratedTexture(Texture2D texture, int payloadBytes)
        {
            if (!Luma4RasterReader.TryReadFrame(
                    texture,
                    ProtocolConstants.DefaultBlockSize,
                    FrameHeader.Size,
                    payloadBytes,
                    out FrameHeader header,
                    out byte[] payload,
                    out string error))
            {
                Debug.LogError($"[TSMP] sample validation failed: {error}");
                return false;
            }

            if (!NetworkFrameReader.TryReadNetworkFrameHeader(
                    payload,
                    out int messageCount,
                    out _))
            {
                Debug.LogError("[TSMP] sample validation failed: network frame header is invalid.");
                return false;
            }

            Debug.Log(
                "[TSMP] sample validation passed. "
                + $"frameIndex={header.FrameIndex}, "
                + $"payloadBytes={payload.Length}, "
                + $"messageCount={messageCount}");

            return true;
        }

        private static int BuildNetworkSamplePayload(byte[] payload)
        {
            int offset = NetworkFrameWriter.BeginNetworkFrame(payload, 0, 1);
            if (offset < 0)
                return 0;

            int messageCount = 0;
            int variableMessageStart = offset;
            offset = NetworkFrameWriter.BeginVariableState(payload, offset, 1, 1);
            if (offset < 0)
                return 0;

            int variableCount = 0;
            offset = WriteSampleVariable(
                payload,
                offset,
                StableHash.Fnv1A32("sample.health"),
                NetworkValueType.Int32,
                100);
            if (offset < 0)
                return 0;
            variableCount++;

            offset = WriteSampleVariable(
                payload,
                offset,
                StableHash.Fnv1A32("sample.position"),
                NetworkValueType.Vector3,
                new Vector3(1f, 2f, 3f));
            if (offset < 0)
                return 0;
            variableCount++;

            if (!NetworkFrameWriter.EndVariableState(payload, variableMessageStart, offset, variableCount))
                return 0;
            messageCount++;

            int rpcMessageStart = offset;
            offset = NetworkFrameWriter.BeginRpcCall(
                payload,
                offset,
                1,
                1,
                StableHash.Fnv1A32("sample.rpc"));
            if (offset < 0)
                return 0;

            int argumentCount = 0;
            offset = WriteSampleRpcArgument(payload, offset, NetworkValueType.Float32, 0.5f);
            if (offset < 0)
                return 0;
            argumentCount++;

            if (!NetworkFrameWriter.EndRpcCall(payload, rpcMessageStart, offset, argumentCount))
                return 0;
            messageCount++;

            if (!NetworkFrameWriter.EndNetworkFrame(payload, 0, messageCount))
                return 0;

            return offset;
        }

        private static int WriteSampleVariable(
            byte[] payload,
            int offset,
            uint variableHash,
            NetworkValueType valueType,
            object value)
        {
            return NetworkValueEntryWriter.WriteVariableValue(payload, offset, variableHash, (int)valueType, value);
        }

        private static int WriteSampleRpcArgument(
            byte[] payload,
            int offset,
            NetworkValueType valueType,
            object value)
        {
            return NetworkValueEntryWriter.WriteRpcArgument(payload, offset, (int)valueType, value);
        }
    }
}
