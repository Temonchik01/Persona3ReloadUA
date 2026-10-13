namespace P3RBmdTool;

// ── Relocation table RLE encoder / decoder ────────────────────────────────────
//
// Ported from AtlusScriptToolchain RelocationTableEncoding.cs.
// Records file positions (absolute) of all int32 pointer fields.
// addressBaseOffset = BinaryHeader.SIZE = 32 for all P3R BMDs.
//
// Encoding format:
//   Even byte  → delta/2 from previous (shift right 1 to recover delta)
//   Odd, low bits != 0x07 → extended 2-byte form: lo-1 | (next_byte << 8)
//   Odd, low bits == 0x07 → sequence run of consecutive addresses
//
// For our purposes we use the simpler EncodeAddress-only path (no sequence
// compression), which is functionally correct even though it may produce
// slightly longer tables than the Atlus tool.

public static class RelocationTableEncoding
{
    const int  ADDR_SIZE     = 4;
    const byte SEQ_BASE      = 0x07;
    const byte SEQ_BASE_LOOP = 2;
    const byte SEQ_FLAG_ODD  = 1 << 3;

    // ── Decode ────────────────────────────────────────────────────────────────
    public static List<int> Decode(byte[] table, int addressBaseOffset)
    {
        var locs = new List<int>();
        int prev = 0;

        for (int i = 0; i < table.Length; i++)
        {
            int reloc = table[i];

            if ((reloc & 1) != 0)
            {
                // Odd byte
                if ((reloc & SEQ_BASE) == SEQ_BASE)
                {
                    // Sequence run of consecutive addresses
                    int baseLoopMult = (reloc & 0xF0) >> 4;
                    int numLoop = SEQ_BASE_LOOP + baseLoopMult * SEQ_BASE_LOOP;
                    if ((reloc & SEQ_FLAG_ODD) != 0) numLoop++;

                    for (int j = 0; j < numLoop; j++)
                    {
                        locs.Add(addressBaseOffset + prev + ADDR_SIZE);
                        prev += ADDR_SIZE;
                    }
                    continue;
                }

                // Extended 2-byte form
                reloc -= 1;
                reloc |= table[++i] << 8;
            }
            else
            {
                reloc <<= 1;
            }

            locs.Add(addressBaseOffset + prev + reloc);
            prev += reloc;
        }

        return locs;
    }

    // ── Encode ────────────────────────────────────────────────────────────────
    public static byte[] Encode(IList<int> addresses, int addressBaseOffset)
    {
        if (addresses.Count == 0)
            return Array.Empty<byte>();

        var sorted = addresses.OrderBy(x => x).ToList();
        var sequences = DetectSequences(sorted);
        var buf = new List<byte>();
        int prevSum = 0;

        for (int idx = 0; idx < sorted.Count; idx++)
        {
            int seqIdx = sequences.FindIndex(s => s.StartIndex == idx);
            int reloc = (sorted[idx] - prevSum) - addressBaseOffset;

            EncodeAddress(reloc, buf, ref prevSum);

            if (seqIdx >= 0)
            {
                var seq = sequences[seqIdx];
                int remaining = seq.Count - 1;

                // Each sequence byte can encode at most 33 consecutive entries
                // (4-bit baseLoopMult maxes at 15 → numLoop=32, +1 odd bit = 33).
                // Split long runs into multiple sequence bytes.
                while (remaining > 0)
                {
                    if (remaining == 1)
                    {
                        // Sequence bytes can't encode a single entry (min=2); emit as a normal delta.
                        EncodeAddress(ADDR_SIZE, buf, ref prevSum);
                        idx++;
                        remaining--;
                    }
                    else
                    {
                        int chunk = Math.Min(remaining, 33);
                        int baseLoopMult = (chunk - SEQ_BASE_LOOP) / SEQ_BASE_LOOP;
                        bool isOdd = (chunk & 1) != 0;
                        int seqByte = SEQ_BASE | (baseLoopMult << 4);
                        if (isOdd) seqByte |= SEQ_FLAG_ODD;
                        buf.Add((byte)seqByte);
                        idx += chunk;
                        prevSum += chunk * ADDR_SIZE;
                        remaining -= chunk;
                    }
                }
            }
        }

        return buf.ToArray();
    }

    static void EncodeAddress(int reloc, List<byte> buf, ref int sum)
    {
        if ((reloc & 1) == 0)
        {
            int shifted = reloc >> 1;
            if (shifted <= 0xFF)
            {
                buf.Add((byte)shifted);
            }
            else
            {
                ExtendReloc(reloc, buf);
            }
        }
        else
        {
            ExtendReloc(reloc, buf);
        }
        sum += reloc;
    }

    static void ExtendReloc(int reloc, List<byte> buf)
    {
        buf.Add((byte)((reloc & 0xFF) + 1));
        buf.Add((byte)((reloc >> 8) & 0xFF));
    }

    // A "sequence" is 3+ consecutive addresses spaced exactly ADDR_SIZE apart.
    static List<(int StartIndex, int Count)> DetectSequences(List<int> addrs)
    {
        var seqs = new List<(int, int)>();
        for (int i = 0; i < addrs.Count - 1; i++)
        {
            if (addrs[i + 1] - addrs[i] != ADDR_SIZE) continue;

            int start = i;
            int count  = 2;
            i++;  // advance past the initial detected pair (matches the original's addressIndex++)

            while (i + 1 < addrs.Count && addrs[i + 1] - addrs[i] == ADDR_SIZE)
            { count++; i++; }

            if (count > 2)
                seqs.Add((start, count));
        }
        return seqs;
    }
}
