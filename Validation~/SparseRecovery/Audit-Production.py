"""Independently compare strict production CSVs and finite policy counters."""
import argparse
from collections import Counter
import csv
import json
from pathlib import Path


def rows(path, variant):
    result = {}
    for row in csv.DictReader(path.open(newline="", encoding="utf-8-sig")):
        if row["variant"] != variant:
            continue
        key = row["profile"], row["file"]
        if key in result:
            raise ValueError("Duplicate observation " + str(key))
        result[key] = row
    return result


def compare(before, after):
    if before.keys() != after.keys():
        raise ValueError("Input observation sets differ")
    summary = Counter(observations=len(before))
    changes = []
    for key, prior in before.items():
        current = after[key]
        size = int(current["expectedBytes"] or "0")
        was_exact = prior["status"] == "EXACT"
        exact = current["status"] == "EXACT"
        summary["beforeExact"] += was_exact
        summary["afterExact"] += exact
        summary["beforeExactBytes"] += size if was_exact else 0
        summary["afterExactBytes"] += size if exact else 0
        summary["newlyRecovered"] += exact and not was_exact
        summary["previouslyExactLost"] += was_exact and not exact
        summary["wrongAccepted"] += current["status"] in {"WRONG", "CORRUPT"}
        summary["normalSuccessSearched"] += was_exact and int(current["sparseSearches"]) != 0
        summary["sparseSearches"] += int(current["sparseSearches"])
        summary["sparseHypotheses"] += int(current["sparseHypotheses"])
        summary["sparseRecoveries"] += int(current["recoveredSparsePayloads"])
        summary["headerRetries"] += int(current["headerAttempts"])
        summary["payloadRetriesBefore"] += int(prior["payloadAttempts"])
        summary["payloadRetriesAfter"] += int(current["payloadAttempts"])
        summary["maxHypothesesPerInput"] = max(summary["maxHypothesesPerInput"], int(current["sparseHypotheses"]))
        if int(current["sparseHypotheses"]) > 384 or int(current["payloadAttempts"]) > 2:
            raise ValueError("Finite recovery budget exceeded")
        if int(current["recoveredSparsePayloads"]) and not exact:
            raise ValueError("Sparse counter on a non-exact output")
        if exact and not was_exact:
            changes.append(dict(profile=key[0], file=key[1], originalBytes=size,
                                sparseHypotheses=int(current["sparseHypotheses"])))
    summary["additionalExactBytes"] = summary["afterExactBytes"] - summary["beforeExactBytes"]
    for key in ["previouslyExactLost", "wrongAccepted", "normalSuccessSearched", "headerRetries"]:
        if summary[key]:
            raise ValueError("Unexpected " + key + ": " + str(summary[key]))
    if summary["payloadRetriesBefore"] != summary["payloadRetriesAfter"]:
        raise ValueError("Sparse recovery changed GPU retry count")
    if summary["newlyRecovered"] != summary["sparseRecoveries"]:
        raise ValueError("Sparse recovery count differs from exact input gains")
    return dict(summary), changes


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--before", type=Path, required=True)
    parser.add_argument("--after", type=Path, required=True)
    parser.add_argument("--before-variant", required=True)
    parser.add_argument("--after-variant", default="SparseRecovery")
    parser.add_argument("--results", type=Path, required=True)
    args = parser.parse_args()
    summary, changes = compare(rows(args.before, args.before_variant), rows(args.after, args.after_variant))
    summary.update(status="PASS", before=str(args.before.resolve()), after=str(args.after.resolve()),
                   beforeVariant=args.before_variant, afterVariant=args.after_variant,
                   scope="Actual production Native decoder against independent pre-raster payloads; no live service/client")
    args.results.mkdir(parents=True, exist_ok=False)
    (args.results / "summary.json").write_text(json.dumps(summary, indent=2))
    with (args.results / "newly-recovered.csv").open("w", newline="") as stream:
        fields = ["profile", "file", "originalBytes", "sparseHypotheses"]
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(changes)
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
