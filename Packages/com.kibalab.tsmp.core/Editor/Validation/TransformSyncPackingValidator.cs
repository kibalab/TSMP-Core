using System.Collections.Generic;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    public static class TransformSyncPackingValidator
    {
        private const float QuantPositionEpsilon = 0.02f;
        private const float QuantRotationEpsilonDeg = 0.1f;
        private const float QuantScaleEpsilon = 0.005f;
        private const float RawEpsilon = 1e-5f;
        private const int PackedVector3ByteSize = 5;
        private const int PackedQuaternionByteSize = 5;

        [MenuItem("TSMP/Debug/Validate TransformSync Packing")]
        public static void Validate()
        {
            var go = new GameObject("TSMP_TransformSync_Validator");
            try
            {
                var sync = go.AddComponent<TSMPNetworkTransformSync>();
                sync.target = go.transform;
                sync.useLocalSpace = true;

                Vector3 seedPos = new Vector3(1.5f, 2.25f, -3.75f);
                Quaternion seedRot = Quaternion.Euler(15f, -30f, 45f);
                Vector3 seedScale = new Vector3(1.5f, 0.75f, 2.0f);

                var modes = new[]
                {
                    CompressionMode.Off,
                    CompressionMode.RotOnly,
                    CompressionMode.Full,
                };

                var failures = new List<string>();

                foreach (var mode in modes)
                {
                    sync.compressionMode = mode;

                    go.transform.localPosition = seedPos;
                    go.transform.localRotation = seedRot;
                    go.transform.localScale = seedScale;

                    sync.TSMPBeforeEncode();

                    int expectedLen = ExpectedPackedLength(mode);
                    if (sync.packedBytes == null || sync.packedBytes.Length != expectedLen)
                    {
                        failures.Add($"[mode={mode}] length expected={expectedLen} actual={(sync.packedBytes == null ? -1 : sync.packedBytes.Length)}");
                        continue;
                    }

                    byte[] roundTrip = (byte[])sync.packedBytes.Clone();

                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;

                    sync.PackedBytes = roundTrip;

                    if (!PositionMatches(go.transform.localPosition, seedPos, mode, out string posErr))
                        failures.Add($"[mode={mode}] position {posErr}");

                    if (!RotationMatches(go.transform.localRotation, seedRot, mode, out string rotErr))
                        failures.Add($"[mode={mode}] rotation {rotErr}");

                    if (!ScaleMatches(go.transform.localScale, seedScale, mode, out string scaleErr))
                        failures.Add($"[mode={mode}] scale {scaleErr}");
                }

                if (failures.Count > 0)
                {
                    Debug.LogError("[TSMP] TransformSync packing validation failed:\n  " + string.Join("\n  ", failures));
                    return;
                }

                Debug.Log("[TSMP] TransformSync packing validation passed: " + modes.Length + " combinations.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static int ExpectedPackedLength(CompressionMode mode)
        {
            int len = 2;
            len += mode == CompressionMode.Full ? PackedVector3ByteSize : 12;
            len += mode != CompressionMode.Off ? PackedQuaternionByteSize : 16;
            len += mode == CompressionMode.Full ? PackedVector3ByteSize : 12;
            return len;
        }

        private static bool PositionMatches(Vector3 actual, Vector3 expected, CompressionMode mode, out string err)
        {
            float epsilon = mode == CompressionMode.Full ? QuantPositionEpsilon : RawEpsilon;
            float diff = Vector3.Distance(actual, expected);
            err = $"diff={diff:F5} epsilon={epsilon} actual={actual} expected={expected}";
            return diff <= epsilon;
        }

        private static bool RotationMatches(Quaternion actual, Quaternion expected, CompressionMode mode, out string err)
        {
            float epsilon = mode != CompressionMode.Off ? QuantRotationEpsilonDeg : 1e-3f;
            float angle = Quaternion.Angle(actual, expected);
            err = $"angle={angle:F4}deg epsilon={epsilon}deg";
            return angle <= epsilon;
        }

        private static bool ScaleMatches(Vector3 actual, Vector3 expected, CompressionMode mode, out string err)
        {
            float epsilon = mode == CompressionMode.Full ? QuantScaleEpsilon : RawEpsilon;
            float diff = Vector3.Distance(actual, expected);
            err = $"diff={diff:F5} epsilon={epsilon} actual={actual} expected={expected}";
            return diff <= epsilon;
        }
    }
}
