#ifndef TSMP_VALIDATION_LUMA4_RECOVERY_INCLUDED
#define TSMP_VALIDATION_LUMA4_RECOVERY_INCLUDED

#include "Packages/com.kibalab.tsmp.codec.luma4/Runtime/Shaders/Luma4Calibration.cginc"

float _RecoveryMode;

float RecoveryNominalLuma(int symbol)
{
    return (symbol == 15 ? 232.0 : 24.0 + symbol * 14.0) / 255.0;
}

// Predict received pilot centres using all sixteen known grey levels. This is
// a receiver-only hypothesis, used only after the ordinary CRC path has failed.
float2 RecoveryFitParameters()
{
    float sumX = 0.0;
    float sumY = 0.0;
    float sumXX = 0.0;
    float sumXY = 0.0;
    [unroll]
    for (int symbol = 0; symbol < 16; symbol++)
    {
        float x = RecoveryNominalLuma(symbol);
        float y = CalibrationLuma(symbol);
        sumX += x;
        sumY += y;
        sumXX += x * x;
        sumXY += x * y;
    }
    float denominator = sumXX - sumX * sumX / 16.0;
    float slope = (sumXY - sumX * sumY / 16.0) / max(denominator, 0.000001);
    return float2(slope, (sumY - slope * sumX) / 16.0);
}

float RecoveryCalibrationLuma(int symbol, float2 fit)
{
#if defined(TSMP_CALIBRATION_LUT)
    // Preparation already applies the selected hypothesis once. It must never
    // be fitted a second time when consuming its LUT.
    return CalibrationLuma(symbol);
#else
    if (_RecoveryMode > 1.5)
        return RecoveryNominalLuma(symbol);
    if (_RecoveryMode > 0.5 && fit.x > 0.0)
        return fit.x * RecoveryNominalLuma(symbol) + fit.y;
    return CalibrationLuma(symbol);
#endif
}

#endif
