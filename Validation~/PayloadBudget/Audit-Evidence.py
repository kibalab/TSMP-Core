"""Audit dedicated Native/Udon budget evidence against the installed encoder sources."""
import argparse
import hashlib
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--results", type=Path, required=True)
parser.add_argument("--repository", type=Path, required=True)
args = parser.parse_args()
checks = {}
assertions = {}
source_hashes = {}
for name in ("native-final", "vrc-final"):
    folder = args.results / name
    result = (folder / "result.txt").read_text(encoding="utf-8-sig")
    checks[name] = result.splitlines()[0] == "PASS"
    assertions[name] = int(re.search(r"Assertions=(\d+)", result).group(1))
    checks[name + "-compiler"] = not re.search(r"error\s+CS\d+|scripts have compiler errors", (folder / "editor.log").read_text(encoding="utf-8-sig", errors="replace"), re.I)
    for item in json.loads((folder / "runtime-source-hashes.json").read_text(encoding="utf-8-sig")):
        installed = Path(item["Path"])
        suffix = installed.as_posix().split("/Packages/", 1)[1]
        repository_file = args.repository / "Packages" / suffix
        installed_hash = hashlib.sha256(installed.read_bytes()).hexdigest().upper()
        repository_hash = hashlib.sha256(repository_file.read_bytes()).hexdigest().upper()
        checks[name + "-" + suffix] = installed_hash == repository_hash == item["Hash"]
        source_hashes[name + "/" + suffix] = installed_hash
for item in json.loads((args.results / "vrc-initial" / "preserved-before.json").read_text(encoding="utf-8-sig")):
    checks["preserved/" + item["Path"]] = hashlib.sha256(Path(item["Path"]).read_bytes()).hexdigest().upper() == item["Hash"]
result = {"status": "PASS" if all(checks.values()) else "FAIL", "assertions": assertions, "checks": checks, "runtimeSourceHashes": source_hashes,
          "transportGainMeasured": False, "liveVrchatRun": False, "wireFormatChanged": False}
(args.results / "independent-audit.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"status": result["status"], "checks": len(checks), "assertions": sum(assertions.values())}))
raise SystemExit(0 if result["status"] == "PASS" else 1)
