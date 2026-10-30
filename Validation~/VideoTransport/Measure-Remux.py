#!/usr/bin/env python3
"""Compare decoded RGB frames after local FLV/HLS remuxing, without a server.

This measures file packaging only. It does not measure RTMP/RTSP/HTTP network
delivery, a CDN, player buffering, or latency. No media is re-encoded.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import time


def execute(command, directory, name):
    started = time.monotonic()
    result = subprocess.run(command, capture_output=True, timeout=180,
                            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    (directory / (name + ".stdout.txt")).write_bytes(result.stdout)
    (directory / (name + ".stderr.txt")).write_bytes(result.stderr)
    (directory / (name + ".process.json")).write_text(json.dumps(dict(
        command=command, exitCode=result.returncode,
        wallSeconds=time.monotonic() - started), indent=2), encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"Command failed: {directory / (name + '.process.json')}")
    return result.stdout


def frame_hashes(path):
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        fields = [field.strip() for field in line.split(",")]
        if len(fields) != 6:
            raise ValueError(f"Invalid framemd5 row in {path}")
        rows.append((int(fields[4]), fields[5]))
    if not rows:
        raise ValueError(f"No decoded frames in {path}")
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--results", required=True, type=Path)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--ffprobe", default="ffprobe")
    args = parser.parse_args()
    args.input = args.input.resolve(strict=True)
    args.results.mkdir(parents=True, exist_ok=True)
    if any(args.results.iterdir()):
        raise ValueError("Choose an empty results directory")
    summary = dict(scope="Local file remux and decoded RGB frame comparison; no server, CDN or network latency",
                   source=str(args.input), sourceSha256=hashlib.sha256(args.input.read_bytes()).hexdigest(),
                   scriptSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), profiles={})
    try:
        execute([args.ffmpeg, "-version"], args.results, "ffmpeg-version")
        flv = args.results / "video.flv"
        playlist = args.results / "index.m3u8"
        execute([args.ffmpeg, "-n", "-hide_banner", "-loglevel", "info", "-i", str(args.input),
                 "-map", "0:v:0", "-an", "-c:v", "copy", "-f", "flv", str(flv)], args.results, "flv-remux")
        execute([args.ffmpeg, "-n", "-hide_banner", "-loglevel", "info", "-i", str(args.input),
                 "-map", "0:v:0", "-an", "-c:v", "copy", "-f", "hls", "-hls_segment_type", "mpegts",
                 "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename",
                 str(args.results / "segment-%05d.ts"), str(playlist)], args.results, "hls-remux")
        reference = None
        for name, media in [("original", args.input), ("flv", flv), ("hls", playlist)]:
            hashes = args.results / (name + ".framemd5")
            command = [args.ffmpeg, "-n", "-hide_banner", "-loglevel", "info"]
            if name == "hls":
                command += ["-protocol_whitelist", "file,crypto,data"]
            command += ["-i", str(media), "-map", "0:v:0", "-an", "-pix_fmt", "rgb24",
                        "-fps_mode", "passthrough", "-f", "framemd5", str(hashes)]
            execute(command, args.results, name + "-decode-hash")
            rows = frame_hashes(hashes)
            probe = [args.ffprobe, "-v", "error"]
            if name == "hls":
                probe += ["-protocol_whitelist", "file,crypto,data"]
            probe += ["-select_streams", "v:0", "-show_streams", "-show_format", "-of", "json", str(media)]
            execute(probe, args.results, name + "-probe")
            if reference is None:
                reference = rows
            mismatches = sum(a != b for a, b in zip(reference, rows)) + abs(len(reference) - len(rows))
            summary["profiles"][name] = dict(decodedFrames=len(rows), rgbFrameHashMismatches=mismatches,
                                               exactDecodedSequence=mismatches == 0)
        summary["status"] = "PASS" if all(p["exactDecodedSequence"] for p in summary["profiles"].values()) else "FAIL"
    except Exception as error:
        summary.update(status="FAIL", error=str(error))
    (args.results / "result.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))
    return 0 if summary["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
