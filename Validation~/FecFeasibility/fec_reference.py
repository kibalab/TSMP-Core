"""Small, measurement-only shortened Reed--Solomon reference over GF(256).

This module is not imported by TSMP runtime code.  Polynomial coefficients are
ordered from highest power to lowest, with primitive polynomial 0x11d and roots
alpha**0 .. alpha**(parity-1).  Decode locates unknown byte errors, solves their
magnitudes, then requires every syndrome to vanish.  An outer original-payload
CRC is still required: a codeword can be miscorrected beyond its distance bound.
"""
from __future__ import annotations

EXP = [0] * 510
LOG = [0] * 256
x = 1
for i in range(255):
    EXP[i] = EXP[i + 255] = x
    LOG[x] = i
    x <<= 1
    if x & 256:
        x ^= 0x11D


class CorrectionFailure(ValueError):
    pass


def mul(a: int, b: int) -> int:
    return EXP[LOG[a] + LOG[b]] if a and b else 0


def div(a: int, b: int) -> int:
    if not b:
        raise ZeroDivisionError()
    return EXP[(LOG[a] - LOG[b]) % 255] if a else 0


def evaluate(poly: list[int] | bytes, at: int) -> int:
    result = 0
    for coefficient in poly:
        result = mul(result, at) ^ coefficient
    return result


def solve(matrix: list[list[int]], vector: list[int]) -> list[int]:
    """Square full-rank GF(256) elimination; used for error magnitudes."""
    n = len(vector)
    work = [row[:] + [value] for row, value in zip(matrix, vector)]
    for column in range(n):
        pivot = next((r for r in range(column, n) if work[r][column]), None)
        if pivot is None:
            raise CorrectionFailure("Singular correction equations")
        work[column], work[pivot] = work[pivot], work[column]
        inverse = div(1, work[column][column])
        work[column] = [mul(value, inverse) for value in work[column]]
        for row in range(n):
            if row != column and work[row][column]:
                factor = work[row][column]
                work[row] = [a ^ mul(factor, b) for a, b in zip(work[row], work[column])]
    return [row[-1] for row in work]


def generator(parity: int) -> list[int]:
    if not 2 <= parity <= 128:
        raise ValueError("Parity must be in 2..128")
    result = [1]
    for power in range(parity):
        product = [0] * (len(result) + 1)
        for i, coefficient in enumerate(result):
            product[i] ^= coefficient
            product[i + 1] ^= mul(coefficient, EXP[power])
        result = product
    return result


def encode(data: bytes, parity: int) -> bytes:
    if not 1 <= len(data) <= 255 - parity:
        raise ValueError("Invalid shortened data length")
    result = bytearray(data) + bytearray(parity)
    polynomial = generator(parity)
    for i in range(len(data)):
        factor = result[i]
        if factor:
            for j, coefficient in enumerate(polynomial):
                result[i + j] ^= mul(coefficient, factor)
    return data + bytes(result[-parity:])


def syndromes(codeword: bytes, parity: int) -> list[int]:
    return [evaluate(codeword, EXP[power]) for power in range(parity)]


def decode(codeword: bytes, parity: int) -> bytes:
    if not parity < len(codeword) <= 255:
        raise ValueError("Invalid shortened codeword length")
    syndrome = syndromes(codeword, parity)
    if not any(syndrome):
        return codeword[:-parity]
    # Berlekamp--Massey, low-to-high locator coefficients.
    locator = [1] + [0] * parity
    prior = locator[:]
    degree, distance, prior_discrepancy = 0, 1, 1
    for step in range(parity):
        discrepancy = syndrome[step]
        for j in range(1, degree + 1):
            discrepancy ^= mul(locator[j], syndrome[step - j])
        if not discrepancy:
            distance += 1
            continue
        saved = locator[:]
        scale = div(discrepancy, prior_discrepancy)
        for j in range(parity + 1 - distance):
            locator[j + distance] ^= mul(scale, prior[j])
        if 2 * degree <= step:
            degree = step + 1 - degree
            prior, prior_discrepancy, distance = saved, discrepancy, 1
        else:
            distance += 1
    if not degree or 2 * degree > parity:
        raise CorrectionFailure("Outside unknown-error correction bound")
    positions = []
    roots = []
    for position in range(len(codeword)):
        power = len(codeword) - 1 - position
        inverse = EXP[(-power) % 255]
        if not evaluate(list(reversed(locator[:degree + 1])), inverse):
            positions.append(position)
            roots.append(EXP[power])
    if len(positions) != degree:
        raise CorrectionFailure("Locator roots outside shortened word")
    matrix = [[1 if power == 0 else EXP[(LOG[root] * power) % 255]
               for root in roots] for power in range(degree)]
    magnitudes = solve(matrix, syndrome[:degree])
    corrected = bytearray(codeword)
    for position, magnitude in zip(positions, magnitudes):
        corrected[position] ^= magnitude
    if any(syndromes(corrected, parity)):
        raise CorrectionFailure("Nonzero syndrome after correction")
    return bytes(corrected[:-parity])


def encode_independent(data: bytes, parity: int) -> bytes:
    """Independent parity calculation from syndrome equations for selftests."""
    n = len(data) + parity
    vector = []
    matrix = []
    for power in range(parity):
        at = EXP[power]
        vector.append(evaluate(data + bytes(parity), at))
        matrix.append([EXP[((parity - 1 - column) * power) % 255]
                       for column in range(parity)])
    return data + bytes(solve(matrix, vector))
