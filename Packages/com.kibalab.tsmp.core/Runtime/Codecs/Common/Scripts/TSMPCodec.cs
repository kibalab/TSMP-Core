using UnityEngine;

namespace K13A.TSMP
{
#if !COMPILER_UDONSHARP
    public struct CodecMaterialContext
    {
        public Texture SourceTexture;
        public FrameLayout FrameLayout;
        public bool FlipY;
        public int OutputWidth;
        public int OutputHeight;
        public int SampleSize;
    }
#endif

    public abstract class TSMPCodec : TSMPBehaviour
    {
        public const string EncoderRequestWidthFieldName = nameof(encoderRequestWidth);
        public const string EncoderRequestHeightFieldName = nameof(encoderRequestHeight);
        public const string EncoderRequestBlockSizeFieldName = nameof(encoderRequestBlockSize);
        public const string EncoderPixelsFieldName = nameof(encoderPixels);
        public const string EncoderPixelsAreBlocksFieldName = nameof(encoderPixelsAreBlocks);
        public const string EncoderPayloadBytesFieldName = nameof(encoderPayloadBytes);
        public const string EncoderPayloadByteCountFieldName = nameof(encoderPayloadByteCount);
        public const string EncoderWriteResultFieldName = nameof(encoderWriteResult);
        public const string EncoderCodecIdFieldName = nameof(encoderCodecId);
        public const string EncoderSymbolModeFieldName = nameof(encoderSymbolMode);
        public const string EncoderPayloadStartRowFieldName = nameof(encoderPayloadStartRow);
        public const string EncoderPayloadCapacityBytesFieldName = nameof(encoderPayloadCapacityBytes);
        public const string EncoderCodecOptionByteCountFieldName = nameof(encoderCodecOptionByteCount);
        public const string EncoderCodecOptionByte0FieldName = nameof(encoderCodecOptionByte0);
        public const string EncoderCodecOptionByte1FieldName = nameof(encoderCodecOptionByte1);
        public const string EncoderCodecOptionByte2FieldName = nameof(encoderCodecOptionByte2);
        public const string EncoderCodecOptionByte3FieldName = nameof(encoderCodecOptionByte3);
        public const string EncoderCodecOptionByte4FieldName = nameof(encoderCodecOptionByte4);
        public const string OnEncoderQueryEventName = nameof(OnTSMPEncoderQuery);
        public const string OnEncoderWritePayloadEventName = nameof(OnTSMPEncoderWritePayload);
        public const int EncoderCodecOptionByteCapacity = 5;

        public ushort codecId;
        public string displayName;
        public byte[] codecOptionBytes;
        public int activeWidthBlocks;
        public int calibrationStartBlock;
        public int decodeStage;
        public bool payloadInterleaved;
        public Material selectedDecodeMaterial;
        public int payloadStartRow = 5;
        public int payloadBlockCount;
        public int byteCount;
        [HideInInspector] public int encoderRequestWidth;
        [HideInInspector] public int encoderRequestHeight;
        [HideInInspector] public int encoderRequestBlockSize;
        [HideInInspector] public byte[] encoderPayloadBytes;
        [HideInInspector] public int encoderPayloadByteCount;
        [HideInInspector] public Color32[] encoderPixels;
        [HideInInspector] public bool encoderPixelsAreBlocks;
        [HideInInspector] public int encoderCodecId;
        [HideInInspector] public int encoderSymbolMode;
        [HideInInspector] public int encoderPayloadStartRow;
        [HideInInspector] public int encoderPayloadCapacityBytes;
        [HideInInspector] public int encoderCodecOptionByteCount;
        [HideInInspector] public int encoderCodecOptionByte0;
        [HideInInspector] public int encoderCodecOptionByte1;
        [HideInInspector] public int encoderCodecOptionByte2;
        [HideInInspector] public int encoderCodecOptionByte3;
        [HideInInspector] public int encoderCodecOptionByte4;
        [HideInInspector] public bool encoderWriteResult;

        public virtual void ApplyDecodeOptions()
        {
        }

        public virtual int GetEncoderSymbolMode()
        {
            return (int)K13A.TSMP.SymbolMode.Luma4;
        }

        public virtual int GetEncoderPayloadStartRow(int width, int blockSize)
        {
            return 5;
        }

        public virtual int GetEncoderPayloadCapacityBytes(int width, int height, int blockSize)
        {
            int activeWidthBlocks = Mathf.Max(0, width / Mathf.Max(1, blockSize));
            int activeHeightBlocks = Mathf.Max(0, height / Mathf.Max(1, blockSize));
            int payloadRows = Mathf.Max(0, activeHeightBlocks - GetEncoderPayloadStartRow(width, blockSize) - 1);
            return activeWidthBlocks * payloadRows * 4 / 8;
        }

        public virtual int GetEncoderCodecOptionByteCount()
        {
            return 0;
        }

        public virtual int GetEncoderCodecOptionByte(int index)
        {
            return 0;
        }

        public static string GetEncoderCodecOptionByteFieldName(int index)
        {
            if (index == 0)
                return EncoderCodecOptionByte0FieldName;
            if (index == 1)
                return EncoderCodecOptionByte1FieldName;
            if (index == 2)
                return EncoderCodecOptionByte2FieldName;
            if (index == 3)
                return EncoderCodecOptionByte3FieldName;
            if (index == 4)
                return EncoderCodecOptionByte4FieldName;

            return null;
        }

        public virtual bool WriteEncoderPayload(Color32[] pixels, int width, int height, int blockSize, byte[] payloadBytes, int payloadByteCount)
        {
            return false;
        }

        protected int ReadCodecOptionByte(int index, int fallback)
        {
            if (codecOptionBytes == null)
                return fallback;
            if (index < 0 || index >= codecOptionBytes.Length)
                return fallback;

            int value = codecOptionBytes[index];
            return value > 0 ? value : fallback;
        }

        protected bool ReadCodecOptionFlag(int index, bool fallback)
        {
            if (codecOptionBytes == null)
                return fallback;
            if (index < 0 || index >= codecOptionBytes.Length)
                return fallback;

            return codecOptionBytes[index] != 0;
        }

        public void OnTSMPEncoderQuery()
        {
            encoderCodecId = codecId;
            encoderSymbolMode = GetEncoderSymbolMode();
            encoderPayloadStartRow = GetEncoderPayloadStartRow(encoderRequestWidth, encoderRequestBlockSize);
            encoderPayloadCapacityBytes = GetEncoderPayloadCapacityBytes(encoderRequestWidth, encoderRequestHeight, encoderRequestBlockSize);
            SetEncoderCodecOptionFields(GetEncoderCodecOptionByteCount());
        }

        public void OnTSMPEncoderWritePayload()
        {
            encoderWriteResult = WriteEncoderPayload(
                encoderPixels,
                encoderRequestWidth,
                encoderRequestHeight,
                encoderRequestBlockSize,
                encoderPayloadBytes,
                encoderPayloadByteCount);
        }

        protected int GetEncoderActiveWidthBlocks(int width, int blockSize)
        {
            return Mathf.Max(0, width / Mathf.Max(1, blockSize));
        }

        protected int GetEncoderActiveHeightBlocks(int height, int blockSize)
        {
            return Mathf.Max(0, height / Mathf.Max(1, blockSize));
        }

        protected void WriteEncoderColorBlockAtIndex(Color32[] pixels, int width, int height, int blockSize, int blockIndex, Color32 color)
        {
            int activeWidthBlocks = GetEncoderActiveWidthBlocks(width, blockSize);
            if (pixels == null || activeWidthBlocks <= 0)
                return;

            int blockX = blockIndex % activeWidthBlocks;
            int blockY = blockIndex / activeWidthBlocks;
            if (encoderPixelsAreBlocks)
            {
                int activeHeightBlocks = GetEncoderActiveHeightBlocks(height, blockSize);
                if (blockX < 0 || blockX >= activeWidthBlocks || blockY < 0 || blockY >= activeHeightBlocks)
                    return;

                int pixelY = activeHeightBlocks - 1 - blockY;
                int pixelIndex = pixelY * activeWidthBlocks + blockX;
                if (pixelIndex >= 0 && pixelIndex < pixels.Length)
                    pixels[pixelIndex] = color;
                return;
            }

            int startX = blockX * blockSize;
            int topY = blockY * blockSize;

            for (int y = 0; y < blockSize; y++)
            {
                int pixelY = height - 1 - (topY + y);
                if (pixelY < 0 || pixelY >= height)
                    continue;

                int rowOffset = pixelY * width;
                for (int x = 0; x < blockSize; x++)
                {
                    int pixelX = startX + x;
                    if (pixelX < 0 || pixelX >= width)
                        continue;

                    pixels[rowOffset + pixelX] = color;
                }
            }
        }

        protected uint ReadEncoderBits(byte[] bytes, int byteCount, int bitOffset, int bitCount)
        {
            int byteIndex = bitOffset / 8;
            int bitShift = bitOffset - byteIndex * 8;
            uint bits = 0u;
            int bytesNeeded = (bitShift + bitCount + 7) / 8;

            for (int i = 0; i < bytesNeeded && i < 4; i++)
            {
                int index = byteIndex + i;
                if (bytes != null && index < byteCount)
                    bits |= (uint)bytes[index] << (i * 8);
            }

            uint mask = bitCount >= 32 ? 0xFFFFFFFFu : (1u << bitCount) - 1u;
            return (bits >> bitShift) & mask;
        }

        private void SetEncoderCodecOptionFields(int count)
        {
            encoderCodecOptionByteCount = Mathf.Clamp(count, 0, EncoderCodecOptionByteCapacity);
            encoderCodecOptionByte0 = encoderCodecOptionByteCount > 0 ? GetEncoderCodecOptionByte(0) : 0;
            encoderCodecOptionByte1 = encoderCodecOptionByteCount > 1 ? GetEncoderCodecOptionByte(1) : 0;
            encoderCodecOptionByte2 = encoderCodecOptionByteCount > 2 ? GetEncoderCodecOptionByte(2) : 0;
            encoderCodecOptionByte3 = encoderCodecOptionByteCount > 3 ? GetEncoderCodecOptionByte(3) : 0;
            encoderCodecOptionByte4 = encoderCodecOptionByteCount > 4 ? GetEncoderCodecOptionByte(4) : 0;
        }

#if !COMPILER_UDONSHARP
        public virtual int SymbolMode => (int)K13A.TSMP.SymbolMode.Luma4;

        public virtual int GetPayloadStartRow(int width, int blockSize)
        {
            return Luma4Raster.PayloadStartRow;
        }

        public virtual int GetPayloadCapacityBytes(int width, int height, int blockSize)
        {
            return Luma4Raster.GetPayloadCapacityBytes(width, height, blockSize);
        }

        public virtual int GetPayloadBlocksForBytes(int byteCount)
        {
            return Luma4Raster.GetPayloadBlocksForBytes(byteCount);
        }

        public virtual bool TryWriteFrame(Texture2D texture, int blockSize, byte[] headerBytes, byte[] payloadBytes, out string error)
        {
            error = "Codec does not implement frame writing.";
            return false;
        }

        protected bool ValidateRasterFrame(Texture2D texture, int blockSize, byte[] headerBytes, byte[] payloadBytes, out string error)
        {
            if (!Luma4Raster.ValidateWrite(texture, blockSize, headerBytes, payloadBytes, out error))
                return false;

            int headerCapacity = Luma4Raster.GetHeaderCapacityBytes(texture.width, blockSize);
            if (headerBytes.Length > headerCapacity)
            {
                error = "Header requires " + headerBytes.Length + " bytes but header region only fits " + headerCapacity + " bytes.";
                return false;
            }

            int capacity = GetPayloadCapacityBytes(texture.width, texture.height, blockSize);
            if (payloadBytes.Length > capacity)
            {
                error = "Payload requires " + payloadBytes.Length + " bytes but payload region only fits " + capacity + " bytes.";
                return false;
            }

            return true;
        }

        public virtual byte[] GetCodecOptionBytes()
        {
            return codecOptionBytes;
        }

        public virtual int DecodeMaterialCount => 0;

        public virtual Material GetDecodeMaterial(int index)
        {
            return null;
        }

        public virtual int DebugMaterialCount => 0;
        public virtual Material GetDebugMaterial(int index) => null;

        public virtual void ConfigureMaterials(CodecMaterialContext context)
        {
            for (int i = 0; i < DecodeMaterialCount; i++)
                ConfigureDecodeMaterial(GetDecodeMaterial(i), context, true);
        }

        protected void ConfigureDecodeMaterial(Material material, CodecMaterialContext context, bool applyStartBlock)
        {
            if (material == null)
                return;

            if (context.SourceTexture != null && material.HasProperty(ShaderProperties.MainTex))
                material.SetTexture(ShaderProperties.MainTex, context.SourceTexture);

            SetFloatIfPresent(material, ShaderProperties.BlockSize, context.FrameLayout.BlockSize);
            SetFloatIfPresent(material, ShaderProperties.SampleSize, context.SampleSize);
            SetFloatIfPresent(material, ShaderProperties.ActiveWidthBlocks, context.FrameLayout.ActiveWidthBlocks);
            SetFloatIfPresent(material, ShaderProperties.SourceWidth, context.FrameLayout.Width);
            SetFloatIfPresent(material, ShaderProperties.SourceHeight, context.FrameLayout.Height);
            SetFloatIfPresent(material, ShaderProperties.OutputWidth, context.OutputWidth);
            SetFloatIfPresent(material, ShaderProperties.OutputHeight, context.OutputHeight);
            SetFloatIfPresent(material, ShaderProperties.FlipY, context.FlipY ? 1f : 0f);

            if (applyStartBlock)
                SetFloatIfPresent(material, ShaderProperties.StartBlock, GetPayloadStartRow(context.FrameLayout.Width, context.FrameLayout.BlockSize) * context.FrameLayout.ActiveWidthBlocks);
        }

        protected void ConfigureMaterialGroup(Material[] materials, CodecMaterialContext context, bool applyStartBlock)
        {
            if (materials == null)
                return;

            for (int i = 0; i < materials.Length; i++)
                ConfigureDecodeMaterial(materials[i], context, applyStartBlock);
        }

        protected static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property))
                material.SetFloat(property, value);
        }
#endif
    }
}
