#if defined(TSMP_CALIBRATION_LUT)
Texture2D<float4> _CalibrationLut;
#endif

float CalibrationLuma(int symbol)
{
#if defined(TSMP_CALIBRATION_LUT)
    return _CalibrationLut.Load(int3(symbol, 0, 0)).r;
#else
    float blockX0 = symbol * 2;
    float blockX1 = blockX0 + 1.0;
    float a = SampleBlockLuma(blockX0, 1.0);
    float b = SampleBlockLuma(blockX1, 1.0);
    return (a + b) * 0.5;
#endif
}
