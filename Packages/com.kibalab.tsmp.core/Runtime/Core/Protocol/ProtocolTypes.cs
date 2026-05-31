using System;

namespace K13A.TSMP
{
    public enum SymbolMode : byte
    {
        Luma4 = 0
    }

    public enum PayloadType : ushort
    {
        Empty = 0x0000,
        NetworkFrame = 0x0100
    }

    public enum NetworkMessageType : byte
    {
        VariableState = 1,
        RpcCall = 2
    }

    public enum NetworkValueType : byte
    {
        Bool = 1,
        Int32 = 2,
        Float32 = 3,
        Vector2 = 4,
        Vector3 = 5,
        Quaternion = 6,
        UTF8String = 7,
        RawBytes = 8,
        BoolArray = 9,
        Int32Array = 10,
        Float32Array = 11,
        Vector2Array = 12,
        Vector3Array = 13,
        QuaternionArray = 14,
        UTF8StringArray = 15
    }

    public enum NetworkSyncDirection : byte
    {
        SendReceive = 0,
        SendOnly = 1,
        ReceiveOnly = 2
    }

    public enum RPCTarget
    {
        Local = 0,
        Remote = 1,
        All = 2
    }

    [Flags]
    public enum FrameFlags : ushort
    {
        None = 0
    }

    public struct ResolutionProfile
    {
        public int Width;
        public int Height;
        public int FrameRate;

        public ResolutionProfile(int width, int height, int frameRate)
        {
            Width = width;
            Height = height;
            FrameRate = frameRate;
        }
    }
}
