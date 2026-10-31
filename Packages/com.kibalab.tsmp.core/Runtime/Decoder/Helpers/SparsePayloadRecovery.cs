namespace K13A.TSMP
{
    // Bounded receiver recovery. No original/source payload is used.
    // Existing modes define the only byte values that may be substituted.
    public static class SparsePayloadRecovery
    {
        public const int MaximumPositions = 8;
        public const int MaximumHypotheses = 384;
        public const int MaximumPayloadBytes = 8192;

        public static uint ByteDifferenceCrc(byte difference, int trailingBytes, uint[] table)
        {
            if (difference == 0 || trailingBytes < 0 || trailingBytes > 65535)
                return 0u;
            return Multiply(SuffixFactor(trailingBytes), table[difference]);
        }

        // Arithmetic is over reflected CRC-32 GF(2), with 0x80000000 as one.
        // x^(8 * 2^bit) powers avoid advancing once per remaining byte.
        private static uint SuffixFactor(int bytes)
        {
            uint factor = 0x80000000u;
            int bit = 0;
            while (bytes > 0)
            {
                if ((bytes & 1) != 0)
                    factor = Multiply(BytePower(bit), factor);
                bytes >>= 1;
                bit++;
            }
            return factor;
        }

        private static uint Multiply(uint left, uint right)
        {
            uint product = 0u;
            while (left != 0u)
            {
                if ((left & 0x80000000u) != 0u)
                    product ^= right;
                left <<= 1;
                right = (right >> 1) ^ ((right & 1u) != 0u ? 0xEDB88320u : 0u);
            }
            return product;
        }

        private static uint BytePower(int bit)
        {
            if (bit == 0) return 0x00800000u;
            if (bit == 1) return 0x00008000u;
            if (bit == 2) return 0xEDB88320u;
            if (bit == 3) return 0xB1E6B092u;
            if (bit == 4) return 0xA06A2517u;
            if (bit == 5) return 0xED627DAEu;
            if (bit == 6) return 0x88D14467u;
            if (bit == 7) return 0xD7BBFE6Au;
            if (bit == 8) return 0xEC447F11u;
            if (bit == 9) return 0x8E7EA170u;
            if (bit == 10) return 0x6427800Eu;
            if (bit == 11) return 0x4D47BAE0u;
            if (bit == 12) return 0x09FE548Fu;
            if (bit == 13) return 0x83852D0Fu;
            if (bit == 14) return 0x30362F1Au;
            return 0x7B5A9CC3u;
        }

        // Returns -1 on overflow. modeOne remembers differences from normal;
        // positions added by mode two necessarily had the normal mode-one byte.
        public static int Collect(byte[] normal, byte[] current, int count, int[] positions,
            byte[] modeOne, int positionOffset, int existing, bool capturingModeOne)
        {
            if (normal == null || current == null || count <= 0 || count > MaximumPayloadBytes ||
                count > normal.Length || count > current.Length || positions == null || modeOne == null ||
                positionOffset < 0 || positions.Length < positionOffset + MaximumPositions || modeOne.Length < positionOffset + MaximumPositions || existing < 0 || existing > MaximumPositions)
                return -1;
            for (int offset = 0; offset < count; offset++)
            {
                // Compare equal runs in one VM extern call per array. Endianness
                // is irrelevant because both words are compared without unpacking.
                if (offset <= count - 8 && System.BitConverter.ToUInt64(normal, offset) == System.BitConverter.ToUInt64(current, offset))
                {
                    offset += 7;
                    continue;
                }
                if (normal[offset] == current[offset])
                    continue;
                bool found = false;
                for (int i = 0; i < existing; i++)
                    if (positions[positionOffset + i] == offset)
                        found = true;
                if (found)
                    continue;
                if (existing == MaximumPositions)
                    return -1;
                positions[positionOffset + existing] = offset;
                modeOne[positionOffset + existing] = capturingModeOne ? current[offset] : normal[offset];
                existing++;
            }
            return existing;
        }

        // Modifies current only after exactly one distinct candidate matches.
        // The caller must perform its usual full protected-payload validation
        // before acceptance/application, even after a successful return.
        public static bool TryRecover(byte[] normal, byte[] current, int count, int[] positions,
            byte[] modeOne, int positionOffset, int differing, uint normalCrc, uint modeOneCrc, uint currentCrc,
            uint expectedCrc, uint[] table, byte[] values, uint[] effects, byte[] winner,
            out int hypotheses)
        {
            hypotheses = 0;
            if (normal == null || current == null || count <= 0 || count > MaximumPayloadBytes ||
                normal.Length < count || current.Length < count || differing <= 0 || differing > MaximumPositions ||
                positions == null || positionOffset < 0 || positions.Length < positionOffset + differing || modeOne == null || modeOne.Length < positionOffset + differing ||
                values == null || values.Length < MaximumPositions * 3 || effects == null || effects.Length < MaximumPositions * 3 ||
                winner == null || winner.Length < MaximumPositions || table == null ||
                (table.Length != Crc32Runtime.TableSize && table.Length != Crc32Runtime.FastTableSize) ||
                normalCrc == expectedCrc || modeOneCrc == expectedCrc || currentCrc == expectedCrc)
                return false;
            uint checkOne = normalCrc, checkTwo = normalCrc;
            for (int p = 0; p < differing; p++)
            {
                int offset = positions[positionOffset + p];
                if (offset < 0 || offset >= count)
                    return false;
                for (int previous = 0; previous < p; previous++)
                    if (positions[positionOffset + previous] == offset)
                        return false;
                int at = p * 3;
                values[at] = normal[offset];
                values[at + 1] = modeOne[positionOffset + p];
                values[at + 2] = current[offset];
                effects[at] = 0u;
                uint factor = SuffixFactor(count - offset - 1);
                effects[at + 1] = Multiply(factor, table[values[at] ^ values[at + 1]]);
                effects[at + 2] = Multiply(factor, table[values[at] ^ values[at + 2]]);
                checkOne ^= effects[at + 1];
                checkTwo ^= effects[at + 2];
            }
            // Consistency check against the independently computed full CRCs;
            // this is not proof against CRC collisions or source authenticity.
            if (checkOne != modeOneCrc || checkTwo != currentCrc)
                return false;
            bool foundWinner = false;
            for (int basis = 0; basis < 3; basis++)
            {
                uint basisCrc = basis == 0 ? normalCrc : basis == 1 ? modeOneCrc : currentCrc;
                for (int p = 0; p < differing; p++)
                {
                    for (int alternative = 0; alternative < 3; alternative++)
                    {
                        if (!DistinctAlternative(values, p, basis, alternative))
                            continue;
                        uint firstCrc = basisCrc ^ effects[p * 3 + basis] ^ effects[p * 3 + alternative];
                        hypotheses++;
                        if (firstCrc == expectedCrc)
                        {
                            if (!RememberWinner(values, winner, differing, basis, p, alternative, -1, 0, foundWinner))
                                return false;
                            foundWinner = true;
                        }
                        for (int q = p + 1; q < differing; q++)
                        {
                            for (int second = 0; second < 3; second++)
                            {
                                if (!DistinctAlternative(values, q, basis, second))
                                    continue;
                                hypotheses++;
                                uint candidateCrc = firstCrc ^ effects[q * 3 + basis] ^ effects[q * 3 + second];
                                if (candidateCrc == expectedCrc)
                                {
                                    if (!RememberWinner(values, winner, differing, basis, p, alternative, q, second, foundWinner))
                                        return false;
                                    foundWinner = true;
                                }
                            }
                        }
                    }
                }
            }
            if (!foundWinner)
                return false;
            for (int p = 0; p < differing; p++)
                current[positions[positionOffset + p]] = winner[p];
            return true;
        }

        private static bool DistinctAlternative(byte[] values, int position, int basis, int alternative)
        {
            int at = position * 3;
            byte value = values[at + alternative];
            if (value == values[at + basis])
                return false;
            for (int previous = 0; previous < alternative; previous++)
                if (values[at + previous] == value)
                    return false;
            return true;
        }

        private static bool RememberWinner(byte[] values, byte[] winner, int differing, int basis,
            int first, int alternative, int second, int secondAlternative, bool found)
        {
            for (int p = 0; p < differing; p++)
            {
                int mode = p == first ? alternative : p == second ? secondAlternative : basis;
                byte value = values[p * 3 + mode];
                if (found && winner[p] != value)
                    return false;
                if (!found)
                    winner[p] = value;
            }
            return true;
        }
    }
}
