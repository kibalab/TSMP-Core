import argparse
import csv
import math
from pathlib import Path
from statistics import median


def read(path):
    with path.open(encoding="utf-8-sig", newline="") as file:
        return list(csv.DictReader(file))


def stats(values):
    values = sorted(values)
    return [round(median(values), 4), round(values[math.ceil(.95 * len(values)) - 1], 4)]


def summarize(raw, dest, counts):
    with dest.open("w", newline="", encoding="utf-8") as file:
        writer = csv.writer(file, lineterminator="\n")
        writer.writerow("count,mode,payloadBytes,vmSamples,vmP50ms,vmP95ms,nestedPoseP50ms,playerGpuSamples,gpuP50ms,gpuP95ms,gpuA50ms,gpuB50ms,playerMainP50ms,playerMainP95ms,idChangesA,idChangesB".split(","))
        for count in counts:
            vm = read(raw / f"vm-{count}.csv")
            for mode in ("cpu", "gpu", "hybrid"):
                samples = [s for s in vm if s["mode"] == mode]
                if len(samples) != 160 or any(int(s["applied"]) != (0 if mode == "gpu" else count) for s in samples):
                    raise ValueError(f"Invalid VM sample set for {count} {mode}")
                cpu = [(float(s["submitUs"]) + float(s["completionUs"])) / 1000 for s in samples]
                pose = median(float(s["poseUs"]) / 1000 for s in samples)
                gpu, main, changes, runs = [], [], [], []
                for run in ("A", "B"):
                    frames = read(raw / f"{mode}-{count}-{run}.csv")
                    timed = [s for s in frames if s["fresh"] == "True" and float(s["gpuMs"]) > 0]
                    if len(frames) != 160 or len(timed) < 152:
                        raise ValueError(f"No reliable frame timing for {mode}-{count}-{run}")
                    g = [float(s["gpuMs"]) for s in timed]
                    runs.append(round(median(g), 4))
                    gpu.extend(g)
                    main.extend(float(s["mainMs"]) for s in timed)
                    changes.append(int(frames[0]["changes"]))
                writer.writerow([count, mode, 8 + 344 * count, len(cpu), *stats(cpu), round(pose, 4),
                                 len(gpu), *stats(gpu), *runs, *stats(main), *changes])


def plot(data, image):
    import matplotlib
    matplotlib.use("Agg")
    from matplotlib import pyplot as plt
    rows = read(data)
    fig, axes = plt.subplots(2, 1, figsize=(8, 6.8), sharex=True, constrained_layout=True)
    for ax, key, title in zip(axes, ("vmP50ms", "gpuP50ms"),
                              ("Receiver CPU in Udon VM / accepted image", "Whole-frame GPU in standalone Player")):
        for mode, name, color, marker in (("cpu", "Native bones", "#244e88", "o"),
                                          ("hybrid", "Hybrid (5 CPU anchors)", "#ae550d", "s"),
                                          ("gpu", "GPU-only", "#037868", "^")):
            series = sorted((int(r["count"]), float(r[key])) for r in rows if r["mode"] == mode)
            ax.plot(*zip(*series), label=name, color=color, marker=marker, linewidth=2)
        ax.set_title(title, loc="left", fontsize=12)
        ax.set_ylabel("Median (ms)")
        ax.set_ylim(bottom=0)
        ax.grid(axis="y", alpha=.25)
        ax.spines["right"].set_visible(False)
        ax.spines["top"].set_visible(False)
    axes[0].axhline(1000 / 60, color="grey", linestyle=":", label="60 Hz frame budget")
    axes[0].legend(loc="upper left", fontsize=9, frameon=False, ncol=2)
    axes[1].set_xlabel("Independently animated avatars")
    axes[1].set_xticks(sorted({int(r["count"]) for r in rows}))
    fig.savefig(image, dpi=160)
    plt.close(fig)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("raw", type=Path)
    parser.add_argument("dest", type=Path)
    parser.add_argument("--plot", type=Path)
    parser.add_argument("--counts", type=int, nargs="+", default=[1, 8, 16, 32, 64, 80])
    args = parser.parse_args()
    summarize(args.raw, args.dest, args.counts)
    if args.plot:
        plot(args.dest, args.plot)
