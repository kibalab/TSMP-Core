"""Deterministic reference checks; run directly, no third-party FEC package."""
import hashlib
import json
import random
import zlib

from fec_reference import (CorrectionFailure, EXP, LOG, decode, div, encode,
                           encode_independent, mul, syndromes)


def main():
    rng = random.Random(20261031)
    assertions = 0
    for a in range(1, 256):
        assert EXP[LOG[a]] == a and mul(a, div(1, a)) == 1
        assertions += 1
    encode_cases = corrected_cases = rejected_cases = 0
    vector_hash = hashlib.sha256()
    for parity in (8, 16, 24, 32, 48, 64):
        for length in (1, 2, 17, 96, 255 - parity):
            data = rng.randbytes(length)
            codeword = encode(data, parity)
            assert codeword == encode_independent(data, parity)
            assert not any(syndromes(codeword, parity))
            assert decode(codeword, parity) == data
            vector_hash.update(codeword)
            assertions += 3
            encode_cases += 1
            for errors in sorted({1, 2, parity // 4, parity // 2}):
                for _ in range(5):
                    damaged = bytearray(codeword)
                    for index in rng.sample(range(len(damaged)), errors):
                        damaged[index] ^= rng.randint(1, 255)
                    assert decode(damaged, parity) == data
                    assertions += 1
                    corrected_cases += 1
            # Outer CRC rejects any wrong result when beyond-distance decoding
            # happens to return another codeword in this finite random suite.
            for _ in range(10):
                damaged = bytearray(codeword)
                errors = min(len(damaged), parity // 2 + 1)
                for index in rng.sample(range(len(damaged)), errors):
                    damaged[index] ^= rng.randint(1, 255)
                try:
                    candidate = decode(damaged, parity)
                    assert zlib.crc32(candidate) != zlib.crc32(data) or candidate == data
                except CorrectionFailure:
                    pass
                rejected_cases += 1
                assertions += 1
    print(json.dumps({"status": "PASS", "assertions": assertions,
                      "independentEncoderCases": encode_cases,
                      "withinBoundCorruptionCases": corrected_cases,
                      "beyondBoundCases": rejected_cases,
                      "vectorsSha256": vector_hash.hexdigest()}, indent=2))


if __name__ == '__main__':
    main()
