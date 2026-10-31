"""Screen finite byte substitutions using saved GPU outputs; no raster changes.

The candidate generator sees only existing mode outputs and their CRC, never
the independent expected payload. References are used after selection to audit
exactness. This is an offline feasibility measurement, not a receiver replay.
"""
import argparse
import csv
import hashlib
import itertools
import json
from pathlib import Path
import struct
import zlib


def search(outputs, expected_crc, max_positions=8):
    count = len(outputs[0])
    positions = [p for p in range(count) if len({o[p] for o in outputs}) > 1]
    if len(positions) > max_positions:
        return None, 0, len(positions), "POSITION_LIMIT"
    winners = set()
    attempts = 0
    seen = set()
    for original in reversed(outputs):
        alternatives = [(p, value) for p in positions for value in sorted({o[p] for o in outputs}) if value != original[p]]
        edits = [(a,) for a in alternatives]
        edits += [(a, b) for a, b in itertools.combinations(alternatives, 2) if a[0] != b[0]]
        for changes in edits:
            candidate = bytearray(original)
            for p, value in changes:
                candidate[p] = value
            candidate = bytes(candidate)
            if candidate in seen:
                continue
            seen.add(candidate)
            attempts += 1
            if zlib.crc32(candidate) == expected_crc:
                winners.add(candidate)
    return next(iter(winners)) if len(winners) == 1 else None, attempts, len(positions), "UNIQUE" if len(winners) == 1 else "AMBIGUOUS" if winners else "NO_MATCH"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--probe", type=Path, required=True)
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--max-positions", type=int, default=8)
    args = parser.parse_args()
    args.results.mkdir(parents=True, exist_ok=False)
    groups = {}
    for row in csv.DictReader((args.probe / "raw-dumps.csv").open(newline="", encoding="utf-8-sig")):
        groups.setdefault((row["profile"], row["file"]), []).append(row)
    result = []
    for key, rows in groups.items():
        rows.sort(key=lambda r: r["variant"])
        if len(rows) != 3:
            continue
        expected = Path(rows[0]["reference"]).read_bytes()
        outputs = [Path(r["decoded"]).read_bytes() for r in rows]
        if any(len(o) != len(expected) for o in outputs):
            raise RuntimeError("Length mismatch")
        header = Path(rows[0]["header"]).read_bytes()
        if len(header) != 56 or zlib.crc32(header[:52]) != struct.unpack_from("<I", header, 52)[0]:
            raise RuntimeError("Independent protected header invalid")
        crc = struct.unpack_from("<I", header, 44)[0]
        if crc != zlib.crc32(expected):
            raise RuntimeError("Independent header checksum differs from source payload")
        prior_exact = any(o == expected for o in outputs)
        prior_valid = [o for o in outputs if zlib.crc32(o) == crc]
        if prior_valid:
            winner, attempts, positions, status = prior_valid[0], 0, 0, "PRIOR_CRC_VALID"
        else:
            winner, attempts, positions, status = search(outputs, crc, args.max_positions)
        wrong = winner is not None and winner != expected
        result.append(dict(profile=key[0], file=key[1], bytes=len(expected), priorExact=prior_exact,
                           mixedExact=winner == expected, wrongAccepted=wrong, attempts=attempts,
                           differingPositions=positions, status=status,
                           finalByteMismatches=sum(a != b for a, b in zip(outputs[-1], expected))))
    with (args.results / "frames.csv").open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=result[0].keys())
        writer.writeheader()
        writer.writerows(result)
    summary = dict(status="GPU_OUTPUT_OFFLINE_SEARCH_ONLY", inputs=len(result),
                   priorExact=sum(r["priorExact"] for r in result),
                   mixedExact=sum(r["mixedExact"] for r in result),
                   additionalExact=sum(r["mixedExact"] and not r["priorExact"] for r in result),
                   wrongAccepted=sum(r["wrongAccepted"] for r in result),
                   maxCandidates=max(r["attempts"] for r in result),
                   maxPositions=args.max_positions,
                   generatorUsesReferenceBytes=False,
                   expectedChecksumSource="independent protected header; raw diagnostic geometry",
                   codeSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())
    (args.results / "summary.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
