import csv
import os
import sys
import traceback
import renderdoc as rd


def measure(path):
    cap = rd.OpenCaptureFile()
    result = cap.OpenFile(path, "", None)
    if result != rd.ResultCode.Succeeded:
        raise RuntimeError(str(result))
    status, replay = cap.OpenCapture(rd.ReplayOptions(), None)
    if status != rd.ResultCode.Succeeded:
        raise RuntimeError(str(status))
    try:
        counter = rd.GPUCounter.EventGPUDuration
        if counter not in replay.EnumerateCounters():
            raise RuntimeError("No GPU duration counter")
        textures = {t.resourceId: t for t in replay.GetTextures()}
        names = {r.resourceId: r.name for r in replay.GetResources()}
        events = {}

        def walk(actions, parent):
            for a in actions:
                label = a.GetName(replay.GetStructuredFile())
                if a.children:
                    walk(a.children, parent + "/" + label)
                else:
                    target = textures.get(a.outputs[0]) if a.outputs else None
                    events[a.eventId] = (parent, label, a.numIndices,
                        names.get(target.resourceId, "") if target else "",
                        target.width if target else 0, target.height if target else 0)

        walk(replay.GetRootActions(), "")
        with open(path + ".csv", "w", newline="") as f:
            out = csv.writer(f)
            out.writerow(["replay","eid","gpuUs","group","action","indices","target","w","h"])
            for i in range(11):
                for sample in replay.FetchCounters([counter]):
                    if i and sample.eventId in events:
                        out.writerow([i, sample.eventId, 1e6 * sample.value.d] + list(events[sample.eventId]))
    finally:
        replay.Shutdown()
        cap.Shutdown()


try:
    from glob import glob
    files = glob(os.path.join(os.environ["TSMP_SKIN_OUT"], "*-capture*.rdc"))
    if not files:
        raise RuntimeError("No captures")
    for f in files:
        measure(f)
except Exception:
    with open(os.path.join(os.environ["TSMP_SKIN_OUT"], "replay-error.txt"), "w") as f:
        traceback.print_exc(file=f)
    sys.exit(1)
sys.exit(0)
