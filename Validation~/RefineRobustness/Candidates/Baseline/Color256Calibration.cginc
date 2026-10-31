#if defined(TSMP_CALIBRATION_LUT)
Texture2D<float4> _CalibrationLut;
#endif

int MakeColor256Symbol(int r, int g, int b)
{
    return r | (g << 3) | (b << 6);
}

float ChannelValue(float3 c, int channel)
{
    return channel == 0 ? c.r : channel == 1 ? c.g : c.b;
}

float AverageCalibrationChannel(int level, int channel)
{
#if defined(TSMP_CALIBRATION_LUT)
    return _CalibrationLut.Load(int3(256 + (channel == 0 ? 0 : channel == 1 ? 8 : 16) + level, 0, 0)).r;
#else
    float sum = 0.0;
    int count = 0;

    [loop]
    for (int b = 0; b < 4; b++)
    {
        [loop]
        for (int g = 0; g < 8; g++)
        {
[loop]
for (int r = 0; r < 8; r++)
{
    bool include = false;
    if (channel == 0 && r == level) include = true;
    if (channel == 1 && g == level) include = true;
    if (channel == 2 && b == level) include = true;

    if (include)
    {
        int symbol = MakeColor256Symbol(r, g, b);
        sum += ChannelValue(SampleBlockByIndex(_ColorCalibrationStartBlock + symbol), channel);
        count++;
    }
}
        }
    }

    return sum / max(1, count);
#endif
}
