"""Generate the fixed validation probe and freeze its workload before measurement."""
import argparse
import hashlib
import json
from pathlib import Path

here = Path(__file__).resolve().parent
p = argparse.ArgumentParser()
p.add_argument('--plan', type=Path, required=True)
a = p.parse_args()
if a.plan.exists():
    raise SystemExit('Use a fresh plan file')
fields = '\n'.join(f'    [TransSync("binding-cost.{i:03}")] public float f{i:03};' for i in range(128))
source = '''using K13A.TSMP;
using K13A.TSMP.Udon;
using VRC.Udon;

// Validation-only target; values and observations are checked outside timed VM calls.
public sealed class BindingCostProbe : TSMPNetworkBehaviour
{
FIELDS
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
'''.replace('FIELDS', fields)
runtime = here / 'Runtime'
runtime.mkdir(parents=True, exist_ok=True)
probe = runtime / 'BindingCostProbe.cs'
probe.write_text(source, encoding='utf-8')
cases = [dict(name=f'float{n}-targets{targets}-{regime}', fields=n, targets=targets,
              regime=regime, warm=8, samples=32)
         for n in (32, 128) for targets in (1, n)
         for regime in ('changing', 'unchanged', 'quarter-changing')]
plan = dict(schema=1, cases=cases, stopwatch='full SendCustomEvent EncodeNow elapsed; nested VM work counted once',
            scope='Actual compiled client VM in SDK Editor; actual GPU Encoder output; independent protected packet checks and injected Decoder completion, not receiver GPU timing or live VRChat',
            validation='Callback once/order, mixed priority rotation, undue first group field, ReceiveOnly first group field, cross-callback sendMode mutations, callback-time public binding target/direction mutations, in-place same-length replacement; original raw packets retained',
            probe_sha256=hashlib.sha256(probe.read_bytes()).hexdigest())
a.plan.parent.mkdir(parents=True, exist_ok=True)
a.plan.write_text(json.dumps(plan, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'cases': len(cases), 'sha256': hashlib.sha256(a.plan.read_bytes()).hexdigest()}))
