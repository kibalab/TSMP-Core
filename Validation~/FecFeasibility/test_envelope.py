"""Finite mutation/boundary checks for the unshipped measurement envelope."""
import json
import random

from fec_envelope import METADATA_PARITY, PREFIX_BYTES, split_words, unwrap, wrap
from fec_reference import CorrectionFailure


def main():
    rng = random.Random(2026103102)
    checks = 0
    for parity in (8, 16, 32, 48):
        for layout in (0, 1):
            for length in (1, 32, 239, 2495, 5375):
                original = rng.randbytes(length)
                packet = wrap(original, parity, layout)
                assert unwrap(packet) == (original, packet)
                checks += 1
                # Damage data AND parity in every shortened codeword, then
                # verify original bytes and exactly reconstructed outer packet.
                capacity = 255 - parity
                lengths = [min(capacity, length - start) + parity
                           for start in range(0, length, capacity)]
                words = split_words(packet[PREFIX_BYTES:], lengths, layout)
                damaged_words = []
                for word in words:
                    damaged = bytearray(word)
                    positions = {len(word) - 1, 0}
                    positions.update(rng.sample(range(len(word)), max(0, parity // 2 - 2)))
                    for position in positions:
                        damaged[position] ^= rng.randint(1, 255)
                    damaged_words.append(bytes(damaged))
                from fec_envelope import interleave
                body = b''.join(damaged_words) if layout == 0 else interleave(damaged_words)
                assert unwrap(packet[:PREFIX_BYTES] + body) == (original, packet)
                checks += 1
                # Every metadata byte is individually protected.
                for position in range(PREFIX_BYTES):
                    damaged = bytearray(packet)
                    damaged[position] ^= 1
                    try:
                        unwrap(damaged)
                        raise AssertionError('Corrupt metadata accepted')
                    except CorrectionFailure:
                        pass
                    checks += 1
                for truncated in (packet[:-1], packet[:10], packet + b'\0'):
                    try:
                        unwrap(truncated)
                        raise AssertionError('Wrong length accepted')
                    except CorrectionFailure:
                        pass
                    checks += 1
    for length in (65535, 65536):
        try:
            wrap(bytes(length), 64)
            raise AssertionError('Oversize envelope accepted')
        except ValueError:
            pass
        checks += 1
    for parity in (16, 32):
        for length in (32, 2495, 5375):
            original = rng.randbytes(length)
            packet = wrap(original, parity, 1, True)
            assert unwrap(packet, True) == (original, packet)
            checks += 1
            for position in range(PREFIX_BYTES + METADATA_PARITY):
                damaged = bytearray(packet)
                damaged[position] ^= 19
                assert unwrap(damaged, True) == (original, packet)
                checks += 1
            damaged = bytearray(packet)
            for position in rng.sample(range(PREFIX_BYTES + METADATA_PARITY), 8):
                damaged[position] ^= rng.randint(1, 255)
            # Include body corruption in the same packet, not just metadata.
            damaged[PREFIX_BYTES + METADATA_PARITY] ^= 53
            assert unwrap(damaged, True) == (original, packet)
            checks += 1
            for wrong_mode_packet, protected in [(packet, False), (wrap(original, parity), True)]:
                try:
                    unwrap(wrong_mode_packet, protected)
                    raise AssertionError('Wrong envelope version accepted')
                except CorrectionFailure:
                    pass
                checks += 1
    print(json.dumps(dict(status='PASS', assertions=checks,
                          note='Measurement envelope only; not TSMP runtime interoperability.'), indent=2))


if __name__ == '__main__':
    main()
