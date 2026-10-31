using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using K13A.TSMP;

// Pure C# reference checks. Full-payload CRC enumeration is deliberately
// independent of the runtime's CRC-difference search.
public static class SparseRecoveryCases
{
    static int assertions;
    static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    static uint Crc(byte[] bytes, uint[] table) => Crc32Runtime.Compute(bytes, 0, bytes.Length, table);
    public static string Run()
    {
        assertions = 0;
        var random = new Random(20261031);
        uint[] fast = Crc32Runtime.EnsureFastTable(null), small = Crc32Runtime.EnsureTable(null);
        int differences = 0;
        foreach (int length in new[] { 1, 2, 7, 8, 9, 31, 32, 255, 2495, 5375, 8192, 65535 })
        {
            byte[] original = new byte[length]; random.NextBytes(original);
            uint before = Crc(original, fast);
            for (int sample = 0; sample < 96; sample++)
            {
                int offset = sample == 0 ? 0 : sample == 1 ? length - 1 : random.Next(length);
                byte delta = (byte)random.Next(1, 256);
                byte[] changed = (byte[])original.Clone(); changed[offset] ^= delta;
                uint expected = Crc(changed, fast) ^ before;
                Require(SparsePayloadRecovery.ByteDifferenceCrc(delta, length - offset - 1, fast) == expected, "Fast CRC difference mismatch");
                Require(SparsePayloadRecovery.ByteDifferenceCrc(delta, length - offset - 1, small) == expected, "Small CRC difference mismatch");
                differences++;
            }
        }
        int matched = 0;
        for (int trial = 0; trial < 180; trial++)
        {
            int length = trial % 3 == 0 ? 32 : trial % 3 == 1 ? 2495 : 8192;
            byte[] expected = new byte[length]; random.NextBytes(expected);
            var modes = new[] { (byte[])expected.Clone(), (byte[])expected.Clone(), (byte[])expected.Clone() };
            int[] positions = new int[16]; byte[] modeOne = new byte[16];
            int varying = 1 + random.Next(8);
            for (int p = 0; p < varying; p++)
            {
                int at = p * (length / 9);
                for (int mode = 0; mode < 3; mode++)
                    if (random.Next(3) != 0)
                        modes[mode][at] ^= (byte)(1 + random.Next(3));
            }
            uint crc = Crc(expected, fast);
            if (modes.Any(mode => Crc(mode, fast) == crc)) continue;
            int count = SparsePayloadRecovery.Collect(modes[0], modes[1], length, positions, modeOne, 8, 0, true);
            count = SparsePayloadRecovery.Collect(modes[0], modes[2], length, positions, modeOne, 8, count, false);
            var reference = BruteForce(modes, crc, fast);
            byte[] actual = (byte[])modes[2].Clone();
            bool success = SparsePayloadRecovery.TryRecover(modes[0], actual, length, positions, modeOne, 8, count,
                Crc(modes[0], fast), Crc(modes[1], fast), Crc(modes[2], fast), crc,
                fast, new byte[24], new uint[24], new byte[8], out int hypotheses);
            Require(success == (reference.Count == 1), "Reference candidate decision differs");
            Require(hypotheses <= SparsePayloadRecovery.MaximumHypotheses, "Candidate budget exceeded");
            Require(success ? reference.Single().SequenceEqual(actual) && Crc(actual, fast) == crc : modes[2].SequenceEqual(actual), "Wrong output or rejected candidate mutated payload");
            Require(!success || actual.SequenceEqual(expected), "Wrong accepted original bytes");
            matched++;
        }
        CheckAmbiguous(fast);
        CheckBounds(fast);
        return "PASS\nAssertions=" + assertions + "\nCRC difference vectors=" + differences + "\nFull CRC enumeration comparisons=" + matched;
    }

    static HashSet<byte[]> BruteForce(byte[][] modes, uint expected, uint[] table)
    {
        int[] positions = Enumerable.Range(0, modes[0].Length).Where(p => modes.Select(mode => mode[p]).Distinct().Count() > 1).ToArray();
        var winners = new HashSet<byte[]>(new ByteComparer());
        foreach (byte[] basis in modes)
        {
            for (int p = 0; p < positions.Length; p++)
            {
                int offset = positions[p];
                foreach (byte first in modes.Select(mode => mode[offset]).Distinct().Where(value => value != basis[offset]))
                {
                    byte[] candidate = (byte[])basis.Clone(); candidate[offset] = first;
                    if (Crc(candidate, table) == expected) winners.Add((byte[])candidate.Clone());
                    for (int q = p + 1; q < positions.Length; q++)
                    {
                        int secondOffset = positions[q];
                        foreach (byte second in modes.Select(mode => mode[secondOffset]).Distinct().Where(value => value != basis[secondOffset]))
                        {
                            candidate[secondOffset] = second;
                            if (Crc(candidate, table) == expected) winners.Add((byte[])candidate.Clone());
                        }
                        candidate[secondOffset] = basis[secondOffset];
                    }
                }
            }
        }
        return winners;
    }
    static void CheckAmbiguous(uint[] table)
    {
        byte[] first = new byte[32], second = new byte[32];
        byte[] nullDifference = { 1, 150, 48, 7, 119 };
        Array.Copy(nullDifference, second, 5);
        uint expected = Crc(first, table);
        Require(expected == Crc(second, table), "Independent CRC-null fixture changed");
        var modes = new[] { (byte[])first.Clone(), (byte[])first.Clone(), (byte[])second.Clone() };
        modes[0][0] = second[0]; modes[0][1] = second[1];
        modes[1][2] = second[2]; modes[1][3] = second[3];
        modes[2][0] = first[0]; modes[2][1] = first[1];
        int[] positions = new int[8]; byte[] modeOne = new byte[8];
        int count = SparsePayloadRecovery.Collect(modes[0], modes[1], 32, positions, modeOne, 0, 0, true);
        count = SparsePayloadRecovery.Collect(modes[0], modes[2], 32, positions, modeOne, 0, count, false);
        byte[] actual = (byte[])modes[2].Clone();
        Require(BruteForce(modes, expected, table).Count >= 2, "Collision fixture must contain multiple candidates");
        Require(!SparsePayloadRecovery.TryRecover(modes[0], actual, 32, positions, modeOne, 0, count,
            Crc(modes[0], table), Crc(modes[1], table), Crc(modes[2], table), expected,
            table, new byte[24], new uint[24], new byte[8], out _), "Ambiguous CRC candidates accepted");
        Require(actual.SequenceEqual(modes[2]), "Ambiguous rejection mutated payload");
    }
    static void CheckBounds(uint[] table)
    {
        byte[] normal = new byte[8193], other = new byte[8193];
        for (int i = 0; i < 9; i++) other[i] = 1;
        Require(SparsePayloadRecovery.Collect(normal, other, 32, new int[8], new byte[8], 0, 0, true) == -1, "Nine disagreements accepted");
        Require(SparsePayloadRecovery.Collect(normal, other, 8193, new int[8], new byte[8], 0, 0, true) == -1, "Oversized payload accepted");
        Require(SparsePayloadRecovery.Collect(normal, other, 32, new int[8], new byte[8], 1, 0, true) == -1, "Invalid slot slice accepted");
    }
    sealed class ByteComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] a, byte[] b) => a.SequenceEqual(b);
        public int GetHashCode(byte[] bytes) { unchecked { int result = 17; foreach (byte value in bytes) result = result * 31 + value; return result; } }
    }
}
