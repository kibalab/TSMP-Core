"""Encode/decode finite new-format packets using existing exact FFmpeg profiles.

Source parsing checks independent bins, not a color-symbol CPU model.  PNG
controls and every restored input still require the actual GPU shaders.
"""
import argparse
import csv
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import subprocess
import time
import zlib


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command, directory, name):
    started = time.time()
    result = subprocess.run(command, capture_output=True, text=True, timeout=300)
    (directory / (name + '.stdout.txt')).write_text(result.stdout, encoding='utf-8')
    (directory / (name + '.stderr.txt')).write_text(result.stderr, encoding='utf-8')
    record = dict(command=command, exitCode=result.returncode, seconds=time.time() - started)
    (directory / (name + '.json')).write_text(json.dumps(record, indent=2), encoding='utf-8')
    if result.returncode:
        raise RuntimeError(name + ' failed; preserve output logs')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--codec-prefab', required=True)
    parser.add_argument('--profiles', default='x264-veryfast-cbr3.5M-709limited,x264-veryfast-crf18-709limited')
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--ffmpeg', default=r'C:\Windows\ffmpeg.exe')
    parser.add_argument('--ffprobe', default=r'C:\Windows\ffprobe.exe')
    args = parser.parse_args()
    args.results.mkdir(parents=True, exist_ok=False)
    helper = Path(__file__).resolve().parent.parent / 'VideoTransport' / 'Measure-CodecStreaming.py'
    spec = importlib.util.spec_from_file_location('tsmp_codec_encode_reference', helper)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    profiles = module.profiles_for((1280, 720), True, args.profiles)
    mappings, sources = [], []
    paths = sorted((args.fixture / 'input').glob('frame-*.png'))
    assert len(paths) == 90
    for ordinal, path in enumerate(paths, 1):
        assert path.name == f'frame-{ordinal:06d}.png'
        header_path = args.fixture / 'header' / (path.stem + '.bin')
        payload_path = args.fixture / 'payload' / (path.stem + '.bin')
        header, packet = header_path.read_bytes(), payload_path.read_bytes()
        assert len(header) == 56 and zlib.crc32(header[:52]) == struct.unpack_from('<I', header, 52)[0]
        assert len(packet) == struct.unpack_from('<H', header, 42)[0] and zlib.crc32(packet) == struct.unpack_from('<I', header, 44)[0]
        assert struct.unpack_from('<I', header, 24)[0] == ordinal
        mappings.append(dict(file=path.name, expectedFile=path.name, payloadFile=payload_path.name,
                             frameIndex=ordinal, frameIndexSpecified=True))
        sources.append(dict(file=path.name, pngSha256=digest(path), payloadSha256=digest(payload_path),
                            headerSha256=digest(header_path), packetBytes=len(packet)))
    def profile(name, directory):
        return dict(name=args.fixture.name + '__' + name, framesDirectory=str(directory.resolve()),
            expectedDirectory=str((args.fixture / 'input').resolve()),
            expectedPayloadDirectory=str((args.fixture / 'payload').resolve()),
            expectedHeaderDirectory=str((args.fixture / 'header').resolve()),
            codecPrefabPath=args.codec_prefab, frameMappings=mappings)
    manifest = dict(profiles=[profile('png-control', args.fixture / 'input')])
    records = []
    for setting in profiles:
        directory = args.results / setting['name']; (directory / 'decoded').mkdir(parents=True)
        video = directory / 'encoded.mkv'
        command = module.encode_command(args.ffmpeg, args.fixture / 'input', video, len(paths), setting)
        run(command, directory, 'encode')
        decode_filter = module.HELPERS.DECODE_FILTER.format(range=setting['colorRange'])
        run([args.ffmpeg, '-n', '-hide_banner', '-loglevel', 'info', '-i', str(video),
             '-map', '0:v:0', '-vf', decode_filter, '-fps_mode', 'passthrough',
             '-start_number', '1', str(directory / 'decoded' / 'frame-%06d.png')], directory, 'decode')
        assert len(list((directory / 'decoded').glob('frame-*.png'))) == len(paths)
        probe = subprocess.run([args.ffprobe, '-v', 'error', '-show_packets', '-select_streams', 'v:0',
            '-show_entries', 'packet=pts_time,duration_time,size', '-of', 'json', str(video)],
            capture_output=True, text=True, check=True, timeout=60)
        packets = json.loads(probe.stdout)['packets']
        (directory / 'packets.json').write_text(probe.stdout, encoding='utf-8')
        seconds = max(float(p['pts_time']) + float(p.get('duration_time', 1/30)) for p in packets) - min(float(p['pts_time']) for p in packets)
        records.append(dict(profile=setting['name'], videoSha256=digest(video), frames=len(paths),
            packetBytes=sum(int(p['size']) for p in packets), durationSeconds=seconds,
            packetBitrate=sum(int(p['size']) for p in packets) * 8 / seconds))
        manifest['profiles'].append(profile(setting['name'], directory / 'decoded'))
    for source, path in zip(sources, paths):
        assert source['pngSha256'] == digest(path)
        assert source['payloadSha256'] == digest(args.fixture / 'payload' / (path.stem + '.bin'))
        assert source['headerSha256'] == digest(args.fixture / 'header' / (path.stem + '.bin'))
    (args.results / 'transport-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    (args.results / 'summary.json').write_text(json.dumps(dict(status='ENCODED_AWAITING_GPU',
        sources=sources, profiles=records, helperSha256=digest(helper),
        scriptSha256=digest(Path(__file__)), scope='Finite offline files; no live service or application acceptance.'), indent=2), encoding='utf-8')
    print(json.dumps(dict(status='ENCODED_AWAITING_GPU', profiles=records), indent=2), flush=True)


if __name__ == '__main__':
    main()
