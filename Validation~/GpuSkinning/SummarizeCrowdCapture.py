import csv
import sys
from collections import Counter, defaultdict
from pathlib import Path
from statistics import median


def summarize(path):
    times = defaultdict(lambda: defaultdict(float))
    calls = defaultdict(Counter)
    with path.open(newline="") as f:
        for r in csv.DictReader(f):
            group, action = r["group"], r["action"]
            w, h = int(r["w"]), int(r["h"])
            if "RenderLoopJob" in group and "Draw" in action:
                stage = "surface"
            elif "CustomRenderTextures.Update" in group and h == 1 and w in (57, 479, 542) and "Draw" in action:
                stage = "pose" if w == 57 else "matrices"
            elif "ComputeSkinningDispatch" in group and "Dispatch" in action:
                stage = "unitySkin"
            else:
                continue
            i = int(r["replay"])
            times[i][stage] += float(r["gpuUs"])
            calls[i][stage] += 1
            if stage == "surface" and int(r["indices"]) == 0:
                calls[i]["empty"] += 1
    if len(times) != 10 or any(c != calls[1] for c in calls.values()) or calls[1]["pose"] == 0:
        raise ValueError(f"Incomplete or inconsistent counters: {path}")
    stages = ("surface", "pose", "matrices", "unitySkin")
    return [calls[1][s] for s in (*stages, "empty")] + [
        round(median(t[s] for t in times.values()), 3) for s in stages
    ] + [round(median(sum(t.values()) for t in times.values()), 3)]


if __name__ == "__main__":
    out = csv.writer(sys.stdout, lineterminator="\n")
    out.writerow("capture,modelDraws,poseDraws,matrixDraws,skinDispatches,emptyDraws,modelUs,poseUs,matricesUs,unitySkinUs,selectedUs".split(","))
    for name in sys.argv[1:]:
        path = Path(name)
        out.writerow([path.parent.name + "/" + path.name, *summarize(path)])
