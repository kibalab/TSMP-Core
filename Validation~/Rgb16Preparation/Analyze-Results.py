"""Independently recount the saved preparation timings and unchanged receipts."""
import argparse
import csv
import hashlib
import json
from pathlib import Path


def receipts(path):
    rows = json.loads(path.read_text(encoding="utf-8-sig"))
    return {row["Path"]: row["Hash"].lower() for row in rows}


def run(root):
    summary = {"status": "PASS", "scope": "Fresh Native editor processes, existing caches retained", "runs": {}}
    for order in ("fixed-first", "broad-first"):
        folder = root / order
        assert (folder / "result.txt").read_text().splitlines()[0] == "PASS"
        assert receipts(folder / "source-before.json") == receipts(folder / "source-after.json")
        assert receipts(folder / "configuration-before.json") == receipts(folder / "configuration-after.json")
        with (folder / "samples.csv").open(newline="", encoding="utf-8-sig") as stream:
            rows = list(csv.DictReader(stream))
        assert len(rows) == 54
        expected = {(group, str(round_), str(variant), str(mode))
                    for group, variants in (("active-fixed", (1,)), ("broad-final-flush", range(4)), ("broad-each-flush", range(4)))
                    for round_ in range(2) for variant in variants for mode in range(3)}
        assert {(r["group"], r["round"], r["variant"], r["requestedMode"]) for r in rows} == expected
        for row in rows:
            prepare, submit, flush, total = (float(row[k]) for k in ("prepareMs", "blitSubmitMs", "flushAndCompareMs", "totalMs"))
            assert prepare >= 0 and submit >= 0 and total >= 0
            flushed = row["flushed"] == "True"
            assert abs(total - prepare - submit - (flush if flushed else 0)) < 0.00001
            assert row["payload"] == ("EXACT" if flushed else "NOT_READ")
            if not flushed:
                assert row["group"] == "broad-final-flush" and flush == -1
            if row["variant"] == "1":
                assert row["calibrationLut"] == ("False" if row["requestedMode"] == "0" else "True")
        active = [row for row in rows if row["group"] == "active-fixed"]
        broad = {round_: [row for row in rows if row["group"] == "broad-final-flush" and row["round"] == str(round_)] for round_ in range(2)}
        summary["runs"][order] = {
            "draws": len(rows), "flushed_exact_draws": sum(row["flushed"] == "True" for row in rows),
            "active_fixed_mode0_ms": [float(row["totalMs"]) for row in active if row["requestedMode"] == "0"],
            "active_fixed_recovery_ms": {str(mode): [float(row["totalMs"]) for row in active if row["requestedMode"] == str(mode)] for mode in (1, 2)},
            "broad_12_draw_timed_sums_ms": {str(round_): sum(float(row["totalMs"]) for row in samples) for round_, samples in broad.items()},
            "broad_final_flush_ms": {str(round_): float(samples[-1]["flushAndCompareMs"]) for round_, samples in broad.items()},
            "max_prepare_ms": max(float(row["prepareMs"]) for row in rows),
            "max_submit_ms": max(float(row["blitSubmitMs"]) for row in rows),
            "source_file_receipts": len(receipts(folder / "source-before.json")),
            "configuration_receipts": len(receipts(folder / "configuration-before.json")),
            "samples_sha256": hashlib.sha256((folder / "samples.csv").read_bytes()).hexdigest(),
        }
    summary["interpretation"] = "96-second historical broad preparation was not reproduced; cache-empty or actual VRChat startup remains unverified. Timed sums omit configuration/row bookkeeping and are not GPU execution time."
    (root / "independent-summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", type=Path, required=True)
    run(parser.parse_args().results)
