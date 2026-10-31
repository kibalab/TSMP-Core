"""Measurement-only FEC envelope. Not a shipped TSMP protocol extension.

The external TSMP header must identify a new payload type/layout so an old
receiver rejects the packet.  Header parsing and application are deliberately
outside this CPU reference.  Original-payload CRC remains mandatory after RS.
"""
import math
import struct
import zlib

from fec_reference import CorrectionFailure, decode, encode

MAGIC = b'FEC1'
META = struct.Struct('<4sBBBBHHI')
PREFIX_BYTES = META.size + 4
PAYLOAD_TYPE = 0xF100
LAYOUT_ID = 0xFEC1


def interleave(words):
    return bytes(word[column] for column in range(max(map(len, words)))
                 for word in words if column < len(word))


def split_words(body, lengths, layout):
    if layout == 0:
        result, offset = [], 0
        for length in lengths:
            result.append(body[offset:offset + length])
            offset += length
        return result
    words = [bytearray() for _ in lengths]
    offset = 0
    for column in range(max(lengths)):
        for word, length in zip(words, lengths):
            if column < length:
                word.append(body[offset])
                offset += 1
    return list(map(bytes, words))


def wrap(original, parity, layout=1):
    if not 1 <= len(original) <= 65535 or not 2 <= parity <= 64 or parity % 2 or layout not in (0, 1):
        raise ValueError('Invalid FEC prototype geometry')
    capacity = 255 - parity
    words = [encode(original[start:start + capacity], parity)
             for start in range(0, len(original), capacity)]
    metadata = META.pack(MAGIC, 1, parity, layout, 0, len(original), len(words), zlib.crc32(original))
    prefix = metadata + struct.pack('<I', zlib.crc32(metadata))
    body = b''.join(words) if layout == 0 else interleave(words)
    if len(prefix) + len(body) > 65535:
        raise ValueError('FEC prototype exceeds existing uint16 payload size')
    return prefix + body


def unwrap(packet):
    if len(packet) < PREFIX_BYTES:
        raise CorrectionFailure('Truncated FEC metadata')
    metadata = packet[:META.size]
    if zlib.crc32(metadata) != struct.unpack_from('<I', packet, META.size)[0]:
        raise CorrectionFailure('FEC metadata CRC mismatch')
    magic, version, parity, layout, reserved, length, count, original_crc = META.unpack(metadata)
    if magic != MAGIC or version != 1 or reserved or not length or layout not in (0, 1):
        raise CorrectionFailure('Unsupported FEC metadata')
    if not 2 <= parity <= 64 or parity % 2:
        raise CorrectionFailure('Invalid FEC parity count')
    capacity = 255 - parity
    if count != math.ceil(length / capacity) or len(packet) != PREFIX_BYTES + length + parity * count:
        raise CorrectionFailure('Inconsistent FEC length')
    lengths = [min(capacity, length - start) + parity for start in range(0, length, capacity)]
    words = split_words(packet[PREFIX_BYTES:], lengths, layout)
    result = b''.join(decode(word, parity) for word in words)
    if len(result) != length or zlib.crc32(result) != original_crc:
        raise CorrectionFailure('Original payload CRC mismatch after FEC')
    # Re-encode to verify the outer TSMP payload CRC against the protected
    # received header.  A caller must compare this reconstructed packet's CRC.
    return result, wrap(result, parity, layout)
