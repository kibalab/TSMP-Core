"""Prepare immutable experimental envelope bins before production rasterization."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import struct
import zlib

from fec_envelope import (LAYOUT_ID, PAYLOAD_TYPE, PROTECTED_LAYOUT_ID,
                          PROTECTED_PAYLOAD_TYPE, unwrap, wrap)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True, help='Independent payload/header fixture')
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--parity', default='16,32')
    parser.add_argument('--protect-metadata', action='store_true')
    args = parser.parse_args()
    args.results.mkdir(parents=True, exist_ok=False)
    inputs = sorted((args.source / 'payload').glob('frame-*.bin'))
    assert len(inputs) == 90
    fixtures, hashes = [], []
    for parity in map(int, args.parity.split(',')):
        tag = f'rs{parity}-' + ('meta16-' if args.protect_metadata else '') + 'interleaved'
        target = args.results / tag
        for directory in ('header', 'payload'):
            (target / directory).mkdir(parents=True)
        for ordinal, path in enumerate(inputs, 1):
            assert path.stem == f'frame-{ordinal:06d}'
            original = path.read_bytes()
            header = bytearray((args.source / 'header' / path.name).read_bytes())
            assert len(header) == 56 and zlib.crc32(header[:52]) == struct.unpack_from('<I', header, 52)[0]
            assert len(original) == struct.unpack_from('<H', header, 42)[0]
            assert zlib.crc32(original) == struct.unpack_from('<I', header, 44)[0]
            packet = wrap(original, parity, 1, args.protect_metadata)
            assert unwrap(packet, args.protect_metadata) == (original, packet)
            struct.pack_into('<H', header, 18, PROTECTED_LAYOUT_ID if args.protect_metadata else LAYOUT_ID)
            struct.pack_into('<H', header, 40, PROTECTED_PAYLOAD_TYPE if args.protect_metadata else PAYLOAD_TYPE)
            struct.pack_into('<H', header, 42, len(packet))
            struct.pack_into('<I', header, 44, zlib.crc32(packet))
            struct.pack_into('<I', header, 52, zlib.crc32(header[:52]))
            (target / 'payload' / path.name).write_bytes(packet)
            (target / 'header' / path.name).write_bytes(header)
            hashes.append(dict(parity=parity, frame=ordinal, sourcePayload=str(path),
                sourceSha256=hashlib.sha256(original).hexdigest(), originalBytes=len(original),
                packetBytes=len(packet), packetSha256=hashlib.sha256(packet).hexdigest()))
        for codec in ('color256', 'rgb16'):
            fixtures.append(dict(name=f'{codec}-{tag}',
                sourceDirectory=str(target.resolve()),
                codecPrefabPath=f'Packages/com.kibalab.tsmp.codec.{codec}/Runtime/Codec_' +
                    ('Color256' if codec == 'color256' else 'RGB16') + '.prefab'))
    (args.results / 'raster-plan.json').write_text(json.dumps(dict(fixtures=fixtures), indent=2), encoding='utf-8')
    with (args.results / 'source-packets.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(hashes[0])); writer.writeheader(); writer.writerows(hashes)
    print(json.dumps(dict(status='PASS', originalFrames=len(inputs), packetVariants=len(hashes),
        source=str(args.source), newPayloadType=PROTECTED_PAYLOAD_TYPE if args.protect_metadata else PAYLOAD_TYPE,
        newLayoutId=PROTECTED_LAYOUT_ID if args.protect_metadata else LAYOUT_ID,
        scope='Independent envelope bytes prepared; no raster/compression/GPU result.'), indent=2))


if __name__ == '__main__':
    main()
