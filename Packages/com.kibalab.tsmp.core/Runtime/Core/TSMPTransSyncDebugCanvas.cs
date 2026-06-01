using UnityEngine;
using UnityEngine.UI;

#if UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP.Udon
{
    [AddComponentMenu("TSMP/Debug/TransSync Values")]
    public class TSMPTransSyncDebugCanvas : TSMPBehaviour
    {
        public TSMPDecoder decoder;
        public Text targetText;
        public float updateInterval = 0.25f;
        public int maxEntries = 32;
        public int maxArrayElements = 8;
        public int maxByteCount = 96;
        public bool showFieldNames = true;
        public bool showFrameInfo = true;
        public bool sampleOnlyOnDecodedFrame = true;

        private const string Base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        private float _nextUpdateTime;
        private bool _hasSampledFrame;
        private uint _sampledStreamId;
        private uint _sampledFrameIndex;
        private int _sampledAppliedVariableCount;
        private int _cachedEntryCount;
        private int _cachedTotalBindingCount;
        private string[] _cachedEntryTexts;

        private void Start()
        {
            EnsureTextTarget();
        }

        private void Update()
        {
            NormalizeSettings();

            float now = Time.realtimeSinceStartup;
            if (now < _nextUpdateTime)
                return;

            _nextUpdateTime = now + updateInterval;
            RefreshCachedEntriesIfNeeded(false);
            WriteDebugText();
        }

        public void RefreshNow()
        {
            NormalizeSettings();
            RefreshCachedEntriesIfNeeded(true);
            WriteDebugText();
        }

        private void WriteDebugText()
        {
            EnsureTextTarget();
            if (targetText == null)
                return;

            if (decoder == null)
            {
                targetText.text = "TSMP TransSync Values\nDecoder: not assigned";
                return;
            }

            string text = "TSMP TransSync Values\n";
            if (showFrameInfo)
            {
                text += "frame=" + decoder.lastFrameIndex;
                text += " valid=" + BoolText(decoder.lastFrameValid);
                text += " header=" + BoolText(decoder.lastHeaderValid);
                text += " vars=" + decoder.lastAppliedVariableCount;
                text += "\n";
            }

            if (sampleOnlyOnDecodedFrame)
            {
                if (showFrameInfo)
                {
                    text += "sampledFrame=";
                    text += _hasSampledFrame ? _sampledFrameIndex.ToString() : "-";
                    text += " sampledVars=";
                    text += _hasSampledFrame ? _sampledAppliedVariableCount.ToString() : "0";
                    text += "\n";
                }

                if (!_hasSampledFrame)
                {
                    text += "No decoded TransSync frame sampled.";
                    targetText.text = text;
                    return;
                }

                if (_cachedEntryCount <= 0)
                {
                    text += "No TransSync bindings.";
                    targetText.text = text;
                    return;
                }

                for (int i = 0; i < _cachedEntryCount; i++)
                    text += _cachedEntryTexts[i];

                if (_cachedEntryCount < _cachedTotalBindingCount)
                    text += "... " + (_cachedTotalBindingCount - _cachedEntryCount) + " more";

                targetText.text = text;
                return;
            }

            int count = GetBindingCount();
            int visibleCount = count;
            if (visibleCount > maxEntries)
                visibleCount = maxEntries;

            if (visibleCount <= 0)
            {
                text += "No TransSync bindings.";
                targetText.text = text;
                return;
            }

            for (int i = 0; i < visibleCount; i++)
                text += BuildEntryText(i);

            if (visibleCount < count)
                text += "... " + (count - visibleCount) + " more";

            targetText.text = text;
        }

        private void RefreshCachedEntriesIfNeeded(bool force)
        {
            if (!sampleOnlyOnDecodedFrame)
                return;
            if (decoder == null)
                return;
            if (!decoder.lastFrameValid || !decoder.lastHeaderValid)
                return;
            if (decoder.lastAppliedVariableCount <= 0)
                return;

            if (!force && _hasSampledFrame && decoder.lastStreamId == _sampledStreamId && decoder.lastFrameIndex == _sampledFrameIndex && decoder.lastAppliedVariableCount == _sampledAppliedVariableCount)
                return;

            int count = GetBindingCount();
            int visibleCount = count;
            if (visibleCount > maxEntries)
                visibleCount = maxEntries;

            EnsureCachedEntryCapacity(visibleCount);
            for (int i = 0; i < visibleCount; i++)
                _cachedEntryTexts[i] = BuildEntryText(i);

            _cachedEntryCount = visibleCount;
            _cachedTotalBindingCount = count;
            _sampledStreamId = decoder.lastStreamId;
            _sampledFrameIndex = decoder.lastFrameIndex;
            _sampledAppliedVariableCount = decoder.lastAppliedVariableCount;
            _hasSampledFrame = true;
        }

        private void EnsureCachedEntryCapacity(int count)
        {
            if (count <= 0)
                return;
            if (_cachedEntryTexts != null && _cachedEntryTexts.Length >= count)
                return;

            _cachedEntryTexts = new string[count];
        }

        private string BuildEntryText(int index)
        {
            ushort networkId = GetNetworkId(index);
            uint variableHash = GetVariableHash(index);
            int valueType = GetValueType(index);
            string fieldName = GetFieldName(index);
            object value = GetTargetValue(index, fieldName);

            string text = networkId + ":" + variableHash;
            if (showFieldNames && !string.IsNullOrEmpty(fieldName))
                text += " " + fieldName;

            text += " [" + GetTypeName(valueType) + "] ";
            text += FormatValue(value, valueType);
            text += "\n";
            return text;
        }

        private object GetTargetValue(int index, string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return null;

#if UDONSHARP
            UdonBehaviour target = GetUdonTarget(index);
            return GetProgramVariable(target, fieldName);
#else
            Component target = GetComponentTarget(index);
            return GetProgramVariable(target, fieldName);
#endif
        }

#if UDONSHARP
        private UdonBehaviour GetUdonTarget(int index)
        {
            if (decoder.bindingUdonTargets == null)
                return null;
            if (index < 0 || index >= decoder.bindingUdonTargets.Length)
                return null;

            return decoder.bindingUdonTargets[index];
        }
#else
        private Component GetComponentTarget(int index)
        {
            if (decoder.bindingTargets == null)
                return null;
            if (index < 0 || index >= decoder.bindingTargets.Length)
                return null;

            return decoder.bindingTargets[index];
        }
#endif

        private int GetBindingCount()
        {
            if (decoder == null)
                return 0;

            int count = GetArrayLength(decoder.bindingNetworkIds);
            count = Min(count, GetArrayLength(decoder.bindingVariableHashes));
            count = Min(count, GetArrayLength(decoder.bindingValueTypes));
            count = Min(count, GetArrayLength(decoder.bindingFieldNames));
#if UDONSHARP
            count = Min(count, GetArrayLength(decoder.bindingUdonTargets));
#else
            count = Min(count, GetArrayLength(decoder.bindingTargets));
#endif
            return count;
        }

        private ushort GetNetworkId(int index)
        {
            if (decoder.bindingNetworkIds == null || index < 0 || index >= decoder.bindingNetworkIds.Length)
                return 0;

            return decoder.bindingNetworkIds[index];
        }

        private uint GetVariableHash(int index)
        {
            if (decoder.bindingVariableHashes == null || index < 0 || index >= decoder.bindingVariableHashes.Length)
                return 0u;

            return decoder.bindingVariableHashes[index];
        }

        private int GetValueType(int index)
        {
            if (decoder.bindingValueTypes == null || index < 0 || index >= decoder.bindingValueTypes.Length)
                return NetworkFrameProtocol.ValueTypeUnsupported;

            return decoder.bindingValueTypes[index];
        }

        private string GetFieldName(int index)
        {
            if (decoder.bindingFieldNames == null || index < 0 || index >= decoder.bindingFieldNames.Length)
                return string.Empty;

            return decoder.bindingFieldNames[index];
        }

        private string FormatValue(object value, int valueType)
        {
            if (value == null)
                return "<null>";

            if (valueType == NetworkFrameProtocol.ValueTypeBool)
                return (bool)value ? "true" : "false";
            if (valueType == NetworkFrameProtocol.ValueTypeInt32)
                return ((int)value).ToString();
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32)
                return ((float)value).ToString();
            if (valueType == NetworkFrameProtocol.ValueTypeVector2)
                return FormatVector2((Vector2)value);
            if (valueType == NetworkFrameProtocol.ValueTypeVector3)
                return FormatVector3((Vector3)value);
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternion)
                return FormatQuaternion((Quaternion)value);
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8String)
                return "\"" + (string)value + "\"";
            if (valueType == NetworkFrameProtocol.ValueTypeRawBytes)
                return "base64:" + EncodeBytesBase64((byte[])value);
            if (valueType == NetworkFrameProtocol.ValueTypeBoolArray)
                return FormatBoolArray((bool[])value);
            if (valueType == NetworkFrameProtocol.ValueTypeInt32Array)
                return FormatIntArray((int[])value);
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32Array)
                return FormatFloatArray((float[])value);
            if (valueType == NetworkFrameProtocol.ValueTypeVector2Array)
                return FormatVector2Array((Vector2[])value);
            if (valueType == NetworkFrameProtocol.ValueTypeVector3Array)
                return FormatVector3Array((Vector3[])value);
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternionArray)
                return FormatQuaternionArray((Quaternion[])value);
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8StringArray)
                return FormatStringArray((string[])value);

            return "<unsupported>";
        }

        private string EncodeBytesBase64(byte[] bytes)
        {
            if (bytes == null)
                return string.Empty;

            int length = bytes.Length;
            int visibleLength = length;
            if (visibleLength > maxByteCount)
                visibleLength = maxByteCount;

            string value = string.Empty;
            int cursor = 0;
            while (cursor < visibleLength)
            {
                int b0 = bytes[cursor++];
                int b1 = cursor < visibleLength ? bytes[cursor++] : -1;
                int b2 = cursor < visibleLength ? bytes[cursor++] : -1;

                value += Base64Char((b0 >> 2) & 0x3F);
                if (b1 < 0)
                {
                    value += Base64Char((b0 & 0x03) << 4);
                    value += "==";
                }
                else if (b2 < 0)
                {
                    value += Base64Char(((b0 & 0x03) << 4) | ((b1 >> 4) & 0x0F));
                    value += Base64Char((b1 & 0x0F) << 2);
                    value += "=";
                }
                else
                {
                    value += Base64Char(((b0 & 0x03) << 4) | ((b1 >> 4) & 0x0F));
                    value += Base64Char(((b1 & 0x0F) << 2) | ((b2 >> 6) & 0x03));
                    value += Base64Char(b2 & 0x3F);
                }
            }

            if (visibleLength < length)
                value += "...(" + length + " bytes)";

            return value;
        }

        private string Base64Char(int index)
        {
            return Base64Alphabet.Substring(index, 1);
        }

        private string FormatBoolArray(bool[] value)
        {
            if (value == null)
                return "bool[0] []";

            int visible = GetVisibleElementCount(value.Length);
            string text = "bool[" + value.Length + "] [";
            for (int i = 0; i < visible; i++)
            {
                if (i > 0)
                    text += ", ";
                text += value[i] ? "true" : "false";
            }
            return CloseArray(text, visible, value.Length);
        }

        private string FormatIntArray(int[] value)
        {
            if (value == null)
                return "int[0] []";

            int visible = GetVisibleElementCount(value.Length);
            string text = "int[" + value.Length + "] [";
            for (int i = 0; i < visible; i++)
            {
                if (i > 0)
                    text += ", ";
                text += value[i];
            }
            return CloseArray(text, visible, value.Length);
        }

        private string FormatFloatArray(float[] value)
        {
            if (value == null)
                return "float[0] []";

            int visible = GetVisibleElementCount(value.Length);
            string text = "float[" + value.Length + "] [";
            for (int i = 0; i < visible; i++)
            {
                if (i > 0)
                    text += ", ";
                text += value[i];
            }
            return CloseArray(text, visible, value.Length);
        }

        private string FormatVector2Array(Vector2[] value)
        {
            if (value == null)
                return "Vector2[0] []";

            int visible = GetVisibleElementCount(value.Length);
            string text = "Vector2[" + value.Length + "] [";
            for (int i = 0; i < visible; i++)
            {
                if (i > 0)
                    text += ", ";
                text += FormatVector2(value[i]);
            }
            return CloseArray(text, visible, value.Length);
        }

        private string FormatVector3Array(Vector3[] value)
        {
            if (value == null)
                return "Vector3[0] []";

            int visible = GetVisibleElementCount(value.Length);
            string text = "Vector3[" + value.Length + "] [";
            for (int i = 0; i < visible; i++)
            {
                if (i > 0)
                    text += ", ";
                text += FormatVector3(value[i]);
            }
            return CloseArray(text, visible, value.Length);
        }

        private string FormatQuaternionArray(Quaternion[] value)
        {
            if (value == null)
                return "Quaternion[0] []";

            int visible = GetVisibleElementCount(value.Length);
            string text = "Quaternion[" + value.Length + "] [";
            for (int i = 0; i < visible; i++)
            {
                if (i > 0)
                    text += ", ";
                text += FormatQuaternion(value[i]);
            }
            return CloseArray(text, visible, value.Length);
        }

        private string FormatStringArray(string[] value)
        {
            if (value == null)
                return "string[0] []";

            int visible = GetVisibleElementCount(value.Length);
            string text = "string[" + value.Length + "] [";
            for (int i = 0; i < visible; i++)
            {
                if (i > 0)
                    text += ", ";
                text += "\"" + value[i] + "\"";
            }
            return CloseArray(text, visible, value.Length);
        }

        private int GetVisibleElementCount(int count)
        {
            if (count > maxArrayElements)
                return maxArrayElements;
            if (count < 0)
                return 0;

            return count;
        }

        private static string CloseArray(string text, int visible, int count)
        {
            if (visible < count)
                text += ", ...";
            return text + "]";
        }

        private static string FormatVector2(Vector2 value)
        {
            return "(" + value.x + ", " + value.y + ")";
        }

        private static string FormatVector3(Vector3 value)
        {
            return "(" + value.x + ", " + value.y + ", " + value.z + ")";
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return "(" + value.x + ", " + value.y + ", " + value.z + ", " + value.w + ")";
        }

        private static string GetTypeName(int valueType)
        {
            if (valueType == NetworkFrameProtocol.ValueTypeBool)
                return "Bool";
            if (valueType == NetworkFrameProtocol.ValueTypeInt32)
                return "Int32";
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32)
                return "Float32";
            if (valueType == NetworkFrameProtocol.ValueTypeVector2)
                return "Vector2";
            if (valueType == NetworkFrameProtocol.ValueTypeVector3)
                return "Vector3";
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternion)
                return "Quaternion";
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8String)
                return "String";
            if (valueType == NetworkFrameProtocol.ValueTypeRawBytes)
                return "Byte[]";
            if (valueType == NetworkFrameProtocol.ValueTypeBoolArray)
                return "Bool[]";
            if (valueType == NetworkFrameProtocol.ValueTypeInt32Array)
                return "Int32[]";
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32Array)
                return "Float32[]";
            if (valueType == NetworkFrameProtocol.ValueTypeVector2Array)
                return "Vector2[]";
            if (valueType == NetworkFrameProtocol.ValueTypeVector3Array)
                return "Vector3[]";
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternionArray)
                return "Quaternion[]";
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8StringArray)
                return "String[]";

            return "Unknown";
        }

        private string BoolText(bool value)
        {
            return value ? "yes" : "no";
        }

        private void NormalizeSettings()
        {
            if (updateInterval < 0.05f)
                updateInterval = 0.05f;
            if (maxEntries < 1)
                maxEntries = 1;
            if (maxArrayElements < 1)
                maxArrayElements = 1;
            if (maxByteCount < 1)
                maxByteCount = 1;
        }

        private void EnsureTextTarget()
        {
            if (targetText != null)
                return;

            targetText = GetComponent<Text>();
        }

        private static int GetArrayLength(ushort[] value)
        {
            return value != null ? value.Length : 0;
        }

        private static int GetArrayLength(uint[] value)
        {
            return value != null ? value.Length : 0;
        }

        private static int GetArrayLength(byte[] value)
        {
            return value != null ? value.Length : 0;
        }

        private static int GetArrayLength(string[] value)
        {
            return value != null ? value.Length : 0;
        }

#if UDONSHARP
        private static int GetArrayLength(UdonBehaviour[] value)
        {
            return value != null ? value.Length : 0;
        }
#else
        private static int GetArrayLength(Component[] value)
        {
            return value != null ? value.Length : 0;
        }
#endif

        private static int Min(int a, int b)
        {
            return a < b ? a : b;
        }

#if !COMPILER_UDONSHARP
        private void Reset()
        {
            EnsureTextTarget();
        }

        private void OnValidate()
        {
            NormalizeSettings();
            EnsureTextTarget();
        }
#endif
    }
}
