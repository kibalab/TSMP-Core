#if UDONSHARP || COMPILER_UDONSHARP
using K13A.TSMP;
using UdonSharp;

public class SparseRecoveryVmProbe : UdonSharpBehaviour
{
    public byte[] normal, current, modeOne, normalCopy, observedOne;
    public int count, differing, positionOffset;
    public int[] positions;
    public uint normalCrc, modeOneCrc, currentCrc, expectedCrc;
    public uint[] table, effects;
    public byte[] values, winner;
    public bool recovered;
    public int hypotheses;

    public void Recover()
    {
        recovered = SparsePayloadRecovery.TryRecover(normal, current, count, positions, modeOne,
            positionOffset, differing, normalCrc, modeOneCrc, currentCrc, expectedCrc,
            table, values, effects, winner, out hypotheses);
    }

    public void CaptureAndRecover()
    {
        System.Array.Copy(normal, 0, normalCopy, 0, count);
        normal = normalCopy;
        differing = SparsePayloadRecovery.Collect(normal, observedOne, count, positions, modeOne, positionOffset, 0, true);
        differing = SparsePayloadRecovery.Collect(normal, current, count, positions, modeOne, positionOffset, differing, false);
        Recover();
    }

    public void CaptureRecoverAndVerify()
    {
        CaptureAndRecover();
        if (recovered)
            recovered = Crc32Runtime.Compute(current, 0, count, table) == expectedCrc;
    }
}
#endif
