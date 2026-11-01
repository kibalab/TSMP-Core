using K13A.TSMP;
using K13A.TSMP.Udon;
using VRC.Udon;

// Validation-only target; values and observations are checked outside timed VM calls.
public sealed class BindingCostProbe : TSMPNetworkBehaviour
{
    [TransSync("binding-cost.000")] public float f000;
    [TransSync("binding-cost.001")] public float f001;
    [TransSync("binding-cost.002")] public float f002;
    [TransSync("binding-cost.003")] public float f003;
    [TransSync("binding-cost.004")] public float f004;
    [TransSync("binding-cost.005")] public float f005;
    [TransSync("binding-cost.006")] public float f006;
    [TransSync("binding-cost.007")] public float f007;
    [TransSync("binding-cost.008")] public float f008;
    [TransSync("binding-cost.009")] public float f009;
    [TransSync("binding-cost.010")] public float f010;
    [TransSync("binding-cost.011")] public float f011;
    [TransSync("binding-cost.012")] public float f012;
    [TransSync("binding-cost.013")] public float f013;
    [TransSync("binding-cost.014")] public float f014;
    [TransSync("binding-cost.015")] public float f015;
    [TransSync("binding-cost.016")] public float f016;
    [TransSync("binding-cost.017")] public float f017;
    [TransSync("binding-cost.018")] public float f018;
    [TransSync("binding-cost.019")] public float f019;
    [TransSync("binding-cost.020")] public float f020;
    [TransSync("binding-cost.021")] public float f021;
    [TransSync("binding-cost.022")] public float f022;
    [TransSync("binding-cost.023")] public float f023;
    [TransSync("binding-cost.024")] public float f024;
    [TransSync("binding-cost.025")] public float f025;
    [TransSync("binding-cost.026")] public float f026;
    [TransSync("binding-cost.027")] public float f027;
    [TransSync("binding-cost.028")] public float f028;
    [TransSync("binding-cost.029")] public float f029;
    [TransSync("binding-cost.030")] public float f030;
    [TransSync("binding-cost.031")] public float f031;
    [TransSync("binding-cost.032")] public float f032;
    [TransSync("binding-cost.033")] public float f033;
    [TransSync("binding-cost.034")] public float f034;
    [TransSync("binding-cost.035")] public float f035;
    [TransSync("binding-cost.036")] public float f036;
    [TransSync("binding-cost.037")] public float f037;
    [TransSync("binding-cost.038")] public float f038;
    [TransSync("binding-cost.039")] public float f039;
    [TransSync("binding-cost.040")] public float f040;
    [TransSync("binding-cost.041")] public float f041;
    [TransSync("binding-cost.042")] public float f042;
    [TransSync("binding-cost.043")] public float f043;
    [TransSync("binding-cost.044")] public float f044;
    [TransSync("binding-cost.045")] public float f045;
    [TransSync("binding-cost.046")] public float f046;
    [TransSync("binding-cost.047")] public float f047;
    [TransSync("binding-cost.048")] public float f048;
    [TransSync("binding-cost.049")] public float f049;
    [TransSync("binding-cost.050")] public float f050;
    [TransSync("binding-cost.051")] public float f051;
    [TransSync("binding-cost.052")] public float f052;
    [TransSync("binding-cost.053")] public float f053;
    [TransSync("binding-cost.054")] public float f054;
    [TransSync("binding-cost.055")] public float f055;
    [TransSync("binding-cost.056")] public float f056;
    [TransSync("binding-cost.057")] public float f057;
    [TransSync("binding-cost.058")] public float f058;
    [TransSync("binding-cost.059")] public float f059;
    [TransSync("binding-cost.060")] public float f060;
    [TransSync("binding-cost.061")] public float f061;
    [TransSync("binding-cost.062")] public float f062;
    [TransSync("binding-cost.063")] public float f063;
    [TransSync("binding-cost.064")] public float f064;
    [TransSync("binding-cost.065")] public float f065;
    [TransSync("binding-cost.066")] public float f066;
    [TransSync("binding-cost.067")] public float f067;
    [TransSync("binding-cost.068")] public float f068;
    [TransSync("binding-cost.069")] public float f069;
    [TransSync("binding-cost.070")] public float f070;
    [TransSync("binding-cost.071")] public float f071;
    [TransSync("binding-cost.072")] public float f072;
    [TransSync("binding-cost.073")] public float f073;
    [TransSync("binding-cost.074")] public float f074;
    [TransSync("binding-cost.075")] public float f075;
    [TransSync("binding-cost.076")] public float f076;
    [TransSync("binding-cost.077")] public float f077;
    [TransSync("binding-cost.078")] public float f078;
    [TransSync("binding-cost.079")] public float f079;
    [TransSync("binding-cost.080")] public float f080;
    [TransSync("binding-cost.081")] public float f081;
    [TransSync("binding-cost.082")] public float f082;
    [TransSync("binding-cost.083")] public float f083;
    [TransSync("binding-cost.084")] public float f084;
    [TransSync("binding-cost.085")] public float f085;
    [TransSync("binding-cost.086")] public float f086;
    [TransSync("binding-cost.087")] public float f087;
    [TransSync("binding-cost.088")] public float f088;
    [TransSync("binding-cost.089")] public float f089;
    [TransSync("binding-cost.090")] public float f090;
    [TransSync("binding-cost.091")] public float f091;
    [TransSync("binding-cost.092")] public float f092;
    [TransSync("binding-cost.093")] public float f093;
    [TransSync("binding-cost.094")] public float f094;
    [TransSync("binding-cost.095")] public float f095;
    [TransSync("binding-cost.096")] public float f096;
    [TransSync("binding-cost.097")] public float f097;
    [TransSync("binding-cost.098")] public float f098;
    [TransSync("binding-cost.099")] public float f099;
    [TransSync("binding-cost.100")] public float f100;
    [TransSync("binding-cost.101")] public float f101;
    [TransSync("binding-cost.102")] public float f102;
    [TransSync("binding-cost.103")] public float f103;
    [TransSync("binding-cost.104")] public float f104;
    [TransSync("binding-cost.105")] public float f105;
    [TransSync("binding-cost.106")] public float f106;
    [TransSync("binding-cost.107")] public float f107;
    [TransSync("binding-cost.108")] public float f108;
    [TransSync("binding-cost.109")] public float f109;
    [TransSync("binding-cost.110")] public float f110;
    [TransSync("binding-cost.111")] public float f111;
    [TransSync("binding-cost.112")] public float f112;
    [TransSync("binding-cost.113")] public float f113;
    [TransSync("binding-cost.114")] public float f114;
    [TransSync("binding-cost.115")] public float f115;
    [TransSync("binding-cost.116")] public float f116;
    [TransSync("binding-cost.117")] public float f117;
    [TransSync("binding-cost.118")] public float f118;
    [TransSync("binding-cost.119")] public float f119;
    [TransSync("binding-cost.120")] public float f120;
    [TransSync("binding-cost.121")] public float f121;
    [TransSync("binding-cost.122")] public float f122;
    [TransSync("binding-cost.123")] public float f123;
    [TransSync("binding-cost.124")] public float f124;
    [TransSync("binding-cost.125")] public float f125;
    [TransSync("binding-cost.126")] public float f126;
    [TransSync("binding-cost.127")] public float f127;
    public int beforeCount, receivedCount, targetId;
    public UdonBehaviour recorder;
    public bool setModeInBefore;
    public int modeInBefore;
    public bool setOtherMode, mutateBindingTarget, mutateBindingDirection;
    public int otherMode, mutationIndex, directionValue;
    public UdonBehaviour otherTarget, bindingEncoder, replacementTarget;
    public override void TSMPBeforeEncode()
    {
        beforeCount++;
        if (setModeInBefore) sendMode = (SendMode)modeInBefore;
        if (setOtherMode && otherTarget != null) otherTarget.SetProgramVariable("sendMode", otherMode);
        if (mutateBindingTarget && bindingEncoder != null)
        {
            UdonBehaviour[] targets = (UdonBehaviour[])bindingEncoder.GetProgramVariable("bindingUdonTargets");
            targets[mutationIndex] = replacementTarget;
            mutateBindingTarget = false;
        }
        if (mutateBindingDirection && bindingEncoder != null)
        {
            int[] directions = (int[])bindingEncoder.GetProgramVariable("bindingDirections");
            directions[mutationIndex] = directionValue;
            mutateBindingDirection = false;
        }
        if (recorder != null)
        {
            recorder.SetProgramVariable("nextId", targetId);
            recorder.SendCustomEvent("Visit");
        }
    }
    public override void OnTSMPVariableReceived() { receivedCount++; }
    public UdonBehaviour[] scanTargets;
    private UdonBehaviour[] scanCache;
    private int[] scanModes;
    public int scanVisited;
    public void ScanBeforeEncode()
    {
        int count = scanTargets.Length;
        scanCache = EncoderUdonBindingRuntime.EnsureBeforeEncodeTargetCache(scanCache, count);
        if (scanModes == null || scanModes.Length < count) scanModes = new int[count];
        int visited = 0;
        for (int i = 0; i < count; i++)
        {
            int mode;
            visited = EncoderUdonBindingRuntime.SendBeforeEncodeOnce(scanTargets[i], scanCache, visited, scanModes, out mode);
        }
        scanVisited = visited;
    }
    public void DirectBeforeEncode()
    {
        for (int i = 0; i < scanTargets.Length; i++) scanTargets[i].SendCustomEvent("TSMPBeforeEncode");
    }
}
