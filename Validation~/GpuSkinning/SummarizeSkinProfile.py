import argparse
import csv
import math
from collections import defaultdict
from pathlib import Path
from statistics import median


def rows(path):
    with path.open(encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def stats(values):
    return [round(median(values), 4), round(sorted(values)[math.ceil(.95 * len(values)) - 1], 4)]


def emit(path, header, records):
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(header)
        w.writerows(records)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("raw", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    raw, dest = args.raw, args.destination
    dest.mkdir(parents=True, exist_ok=True)
    table = []
    for name in ("decode", "publish", "cpu", "palettes", "gpu", "hybrid"):
        for run in ("presentA", "presentB"):
            base = raw / (name + "-" + run)
            data = rows(base.with_suffix(".csv"))
            info = dict(line.split("=", 1) for line in base.with_suffix(".txt").read_text().splitlines())
            gpu = [float(r["gpuMs"]) for r in data if float(r["gpuMs"]) > 0]
            if len(data) != 240 or info["changes"] != "240/240":
                raise ValueError("Frame count or receive cadence differs: " + str(base))
            if len(gpu) < .95 * len(data):
                raise ValueError("No reliable GPU frame clock: " + str(base))
            table.append([name, run, len(data), info["changes"], info["modelPixels"], len(gpu),
                          *stats(gpu), *stats([float(r["mainMs"]) for r in data if float(r["mainMs"]) >= 0]),
                          *stats([float(r["frameMs"]) for r in data]), *stats([int(r["gcBytes"]) for r in data])])
    emit(dest / "player.csv", ("mode,run,frames,observedIdChanges,modelPixels,gpuSamples,"
                               "gpuP50ms,gpuP95ms,mainP50ms,mainP95ms,intervalP50ms,intervalP95ms,"
                               "gcP50Bytes,gcP95Bytes").split(","), table)
    groups = defaultdict(list)
    for r in rows(raw / "vm.csv"):
        groups[r["mode"]].append(r)
    if set(groups) != {"cpu-default", "cpu", "decode", "gpu", "hybrid"} or any(len(g) != 160 for g in groups.values()):
        raise ValueError("Expected two 80-sample blocks for each Udon case")
    table = []
    for name, data in sorted(groups.items()):
        total = [float(r["submitUs"]) + float(r["completionUs"]) for r in data]
        table.append([name, len(data), *stats(total), *stats([float(r["submitUs"]) for r in data]),
                      *stats([float(r["completionUs"]) for r in data]), *stats([float(r["poseUs"]) for r in data])])
    emit(dest / "vm.csv", ("mode,accepted,totalP50us,totalP95us,submitP50us,submitP95us,callbackP50us,"
                           "callbackP95us,nestedPoseP50us,nestedPoseP95us").split(","), table)
    table = []
    for file in sorted(raw.glob("*-capture*.rdc.csv")):
        name = file.name.split("-")[0]
        by_run = defaultdict(lambda: defaultdict(float))
        draws = defaultdict(int)
        for r in rows(file):
            group, target, action = r["group"], r["target"], r["action"]
            if "RenderLoopJob" in group and "Draw" in action:
                stage = "model"
            elif "ComputeSkinningDispatch" in group and "Dispatch" in action:
                stage = "unitySkin"
            elif target in ("Pose", "Skin Matrices") and "Draw" in action:
                stage = target
            elif (r["w"], r["h"]) == ("256", "1") and "Draw" in action:
                stage = "channel"
            else:
                continue
            i = r["replay"]
            by_run[i][stage] += float(r["gpuUs"])
            if stage == "model":
                draws[i] += 1
        stages = ("model", "unitySkin", "channel", "Pose", "Skin Matrices")
        if len(by_run) != 10 or len(draws) != 10 or min(draws.values()) < 1:
            raise ValueError("Incomplete capture " + file.name)
        table.append([name, len(by_run), max(draws.values()),
                      *[round(median(d[s] for d in by_run.values()), 3) for s in stages],
                      *stats([sum(d.values()) for d in by_run.values()])])
    if sorted(r[0] for r in table) != ["cpu", "gpu", "hybrid"]:
        raise ValueError("Expected exactly one capture for each rendered case")
    emit(dest / "gpu-passes.csv", ("mode,replays,modelDraws,modelP50us,unitySkinP50us,channelP50us,"
                                   "posePaletteP50us,skinPaletteP50us,selectedSumP50us,selectedSumP95us").split(","), table)


if __name__ == "__main__":
    main()
