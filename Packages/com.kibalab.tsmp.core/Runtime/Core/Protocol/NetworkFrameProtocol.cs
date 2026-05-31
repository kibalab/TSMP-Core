namespace K13A.TSMP
{
    public static class NetworkFrameProtocol
    {
        public const byte NetworkVersionMajor = 1;
        public const byte NetworkVersionMinor = 0;
        public const int ByteBytes = 1;
        public const int ByteMaxValue = 255;
        public const int BoolBytes = ByteBytes;
        public const int UInt16Bytes = 2;
        public const int UInt16MaxValue = 65535;
        public const int Int32Bytes = 4;
        public const int UInt32Bytes = 4;
        public const int Float32Bytes = 4;
        public const int Vector2Bytes = 8;
        public const int Vector3Bytes = 12;
        public const int QuaternionBytes = 16;
        public const int Utf8TwoByteSequenceBytes = 2;
        public const int Utf8ThreeByteSequenceBytes = 3;
        public const int Utf8FourByteSequenceBytes = 4;
        public const int Utf8ReplacementBytes = Utf8ThreeByteSequenceBytes;
        public const int NetworkHeaderBytes = 8;
        public const int NetworkVersionMajorOffset = 0;
        public const int NetworkVersionMinorOffset = 1;
        public const int NetworkMessageCountOffset = 2;
        public const int NetworkSequenceOffset = 4;
        public const int MessageHeaderBytes = 8;
        public const int MessageNetworkIdOffset = 0;
        public const int MessageTypeOffset = 2;
        public const int MessageFlagsOffset = 3;
        public const int MessageSequenceOffset = 4;
        public const int VariableValueHeaderBytes = 7;
        public const int VariableStateBodyHeaderBytes = 2;
        public const int VariableValueHashOffset = 0;
        public const int VariableValueTypeOffset = 4;
        public const int VariableValueLengthOffset = 5;
        public const int RpcArgumentHeaderBytes = 3;
        public const int RpcCallBodyHeaderBytes = 5;
        public const int RpcCallHashOffset = 0;
        public const int RpcCallArgumentCountOffset = 4;
        public const int RpcArgumentTypeOffset = 0;
        public const int RpcArgumentLengthOffset = 1;
        public const int MessageBodyLengthOffset = 6;
        public const int VariableCountOffset = 8;
        public const int RpcHashOffset = 8;
        public const int RpcArgumentCountOffset = 12;
        public const int MinimumPayloadBufferBytes = 32;
        public const int MaximumPayloadBytes = UInt16MaxValue;
        public const int PayloadTypeNetworkFrame = 0x0100;

        public const int MessageTypeVariableState = (int)NetworkMessageType.VariableState;
        public const int MessageTypeRpcCall = (int)NetworkMessageType.RpcCall;

        public const int ValueTypeUnsupported = 0;
        public const int ValueTypeBool = (int)NetworkValueType.Bool;
        public const int ValueTypeInt32 = (int)NetworkValueType.Int32;
        public const int ValueTypeFloat32 = (int)NetworkValueType.Float32;
        public const int ValueTypeVector2 = (int)NetworkValueType.Vector2;
        public const int ValueTypeVector3 = (int)NetworkValueType.Vector3;
        public const int ValueTypeQuaternion = (int)NetworkValueType.Quaternion;
        public const int ValueTypeUTF8String = (int)NetworkValueType.UTF8String;
        public const int ValueTypeRawBytes = (int)NetworkValueType.RawBytes;
        public const int ValueTypeBoolArray = (int)NetworkValueType.BoolArray;
        public const int ValueTypeInt32Array = (int)NetworkValueType.Int32Array;
        public const int ValueTypeFloat32Array = (int)NetworkValueType.Float32Array;
        public const int ValueTypeVector2Array = (int)NetworkValueType.Vector2Array;
        public const int ValueTypeVector3Array = (int)NetworkValueType.Vector3Array;
        public const int ValueTypeQuaternionArray = (int)NetworkValueType.QuaternionArray;
        public const int ValueTypeUTF8StringArray = (int)NetworkValueType.UTF8StringArray;
    }
}
