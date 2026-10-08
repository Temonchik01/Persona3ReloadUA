using System.Buffers.Binary;
using System.Text;

namespace P3RBmdTool;

// ── P3R BMD binary writer ─────────────────────────────────────────────────────
//
// Rebuilds the binary from scratch, matching the layout produced by
// AtlusScriptToolchain MessageScriptBinaryBuilder:
//
//   BinaryHeader (32)
//   BinaryDialogHeader × DialogCount (8 each)
//   BinarySpeakerTableHeader (16)
//   Dialog data blocks (variable, 4-byte aligned)
//   Speaker name offset array (4 × SpeakerCount)
//   Speaker name strings (null-terminated)
//   Relocation table (RLE encoded)
//
// All multi-byte values are big-endian.
// Content-base offset = HDR = 32.

public static class P3RBmdWriter
{
    const int HDR = 32;

    public static byte[] Write(P3RBmdFile bmd)
    {
        // ── Layout pass 1: compute positions ─────────────────────────────────
        int mPos = HDR;  // tracks current logical position (from file start)
        var relocs = new List<int>();  // file offsets of all pointer fields

        // Dialog headers
        int[] dialogHeaderPositions = new int[bmd.Dialogs.Length];
        for (int i = 0; i < bmd.Dialogs.Length; i++)
        {
            mPos += 4;  // DialogKind
            relocs.Add(mPos);
            dialogHeaderPositions[i] = mPos;
            mPos += 4;  // Dialog.Offset (pointer)
        }

        // Speaker table header
        relocs.Add(mPos);
        int speakerNameArrayOffsetPos = mPos;
        mPos += 4;  // SpeakerNameArray.Offset (pointer)
        mPos += 4;  // SpeakerCount
        mPos += 4;  // Field08
        mPos += 4;  // Field0C

        // Dialog data (variable, 4-byte aligned)
        var dialogLayouts = new DialogLayout[bmd.Dialogs.Length];
        for (int i = 0; i < bmd.Dialogs.Length; i++)
        {
            // Align to 4 bytes
            mPos = Align4(mPos);

            int dialogAddr = mPos - HDR;   // offset relative to content-base

            var dl = new DialogLayout { AbsoluteOffset = mPos, DialogAddress = dialogAddr };

            switch (bmd.Dialogs[i])
            {
                case P3RMessageDialog msg:
                    ComputeMessageLayout(msg, dialogAddr, dl, relocs, ref mPos);
                    break;
                case P3RSelectionDialog sel:
                    ComputeSelectionLayout(sel, dialogAddr, dl, relocs, ref mPos);
                    break;
            }

            dialogLayouts[i] = dl;
        }

        // Speaker name offset array
        mPos = Align4(mPos);
        int speakerNamesArrayAddr = mPos - HDR;   // relative to content-base

        var speakerNameBytes = new List<(int offset, byte[] data)>();
        {
            int nameWritePos = mPos + bmd.Speakers.Names.Length * 4;  // after the offset array
            for (int i = 0; i < bmd.Speakers.Names.Length; i++)
            {
                relocs.Add(mPos + i * 4);  // position of each name pointer
                int nameOffset = nameWritePos - HDR;
                speakerNameBytes.Add((nameOffset, bmd.Speakers.Names[i].RawBytes));
                nameWritePos += bmd.Speakers.Names[i].RawBytes.Length + 1;  // +1 for null
            }
            mPos += bmd.Speakers.Names.Length * 4;
        }

        // Speaker name strings
        foreach (var (_, nb) in speakerNameBytes)
            mPos += nb.Length + 1;

        // Relocation table
        mPos = Align4(mPos);
        int relTableAbsOffset = mPos;  // ABSOLUTE (not relative to content-base)

        // Debug: count relocs by category
        if (System.Environment.GetEnvironmentVariable("P3R_DEBUG_RELOC") == "1")
        {
            int dialogHdrs = bmd.Dialogs.Length;
            int speakerNms = bmd.Speakers.Names.Length;
            int speakerTbl = 1;
            int pages = relocs.Count - dialogHdrs - speakerTbl - speakerNms;
            Console.Error.WriteLine($"[RELOC] total={relocs.Count}  dialogs={dialogHdrs}  speakerTbl={speakerTbl}  speakerNames={speakerNms}  pages/opts={pages}");
            Console.Error.WriteLine($"[RELOC] relocs[0..4]: {string.Join(", ", relocs.Take(5))}");
            Console.Error.WriteLine($"[RELOC] relocs last5: {string.Join(", ", relocs.TakeLast(5))}");
        }

        byte[] relTableBytes = RelocationTableEncoding.Encode(relocs, HDR);
        mPos += relTableBytes.Length;

        int fileSize = mPos;

        // ── Layout pass 2: write the binary ──────────────────────────────────
        var buf = new byte[fileSize];

        // Header
        buf[0] = 7;   // FILE_TYPE
        buf[1] = (byte)(bmd.IsCompressed ? 1 : 0);
        WriteI16(buf, 2, bmd.UserId);
        WriteI32(buf, 4, fileSize);
        buf[8]  = 0x31; buf[9]  = 0x47; buf[10] = 0x53; buf[11] = 0x4D;  // "1GSM"
        WriteI32(buf, 12, bmd.Field0C);
        WriteI32(buf, 16, relTableAbsOffset);       // RelocationTable.Offset (absolute)
        WriteI32(buf, 20, relTableBytes.Length);    // RelocationTableSize
        WriteI32(buf, 24, bmd.Dialogs.Length);      // DialogCount
        WriteI16(buf, 28, 0);                        // IsRelocated = false
        WriteI16(buf, 30, bmd.Field1E);

        // Dialog headers
        for (int i = 0; i < bmd.Dialogs.Length; i++)
        {
            int hdrPos = HDR + i * 8;
            WriteI32(buf, hdrPos,     (int)bmd.Dialogs[i].Kind);
            WriteI32(buf, hdrPos + 4, dialogLayouts[i].DialogAddress);
        }

        // Speaker table header
        int stPos = HDR + bmd.Dialogs.Length * 8;
        WriteI32(buf, stPos,      speakerNamesArrayAddr);
        WriteI32(buf, stPos + 4,  bmd.Speakers.Names.Length);
        WriteI32(buf, stPos + 8,  bmd.Speakers.Field08);
        WriteI32(buf, stPos + 12, bmd.Speakers.Field0C);

        // Dialog data
        for (int i = 0; i < bmd.Dialogs.Length; i++)
        {
            var dl = dialogLayouts[i];
            switch (bmd.Dialogs[i])
            {
                case P3RMessageDialog msg:
                    WriteMessageDialog(buf, dl, msg);
                    break;
                case P3RSelectionDialog sel:
                    WriteSelectionDialog(buf, dl, sel);
                    break;
            }
        }

        // Speaker name offset array + strings
        int snArrayAbs = HDR + speakerNamesArrayAddr;
        int snWritePos = snArrayAbs + bmd.Speakers.Names.Length * 4;
        for (int i = 0; i < speakerNameBytes.Count; i++)
        {
            var (nameOff, nameData) = speakerNameBytes[i];
            WriteI32(buf, snArrayAbs + i * 4, nameOff);
            nameData.CopyTo(buf, HDR + nameOff);
            buf[HDR + nameOff + nameData.Length] = 0;  // null terminator
        }

        // Relocation table
        relTableBytes.CopyTo(buf, relTableAbsOffset);

        return buf;
    }

    // ── Compute layout for message dialog ─────────────────────────────────────
    static void ComputeMessageLayout(P3RMessageDialog msg, int dialogAddr,
                                      DialogLayout dl, List<int> relocs, ref int mPos)
    {
        dl.NameLength  = 24;
        dl.HeaderFixed = 24 + 2 + 2;  // Name(24) + PageCount(2) + SpeakerId(2)

        int pageCount = msg.Pages.Length;
        mPos += 24 + 2 + 2;  // Name, PageCount, SpeakerId

        // PageStartAddresses — these are pointers, so their FILE positions go in relocs
        int[] pageAddrs = new int[pageCount];
        if (pageCount > 0)
        {
            int textBufOffset = 0x1C + pageCount * 4 + 4;  // relative to dialog block start
            int accumulate    = textBufOffset;

            for (int p = 0; p < pageCount; p++)
            {
                relocs.Add(mPos);         // position of this pointer
                pageAddrs[p] = accumulate + dialogAddr;  // absolute from content-base
                mPos += 4;
                accumulate += msg.Pages[p].ToBytes().Length;
            }
        }

        // TextBufferSize + TextBuffer only written when PageCount > 0 (matches Atlus format)
        if (pageCount > 0)
        {
            var textBuf = BuildMessageTextBuffer(msg.Pages);
            dl.TextBuffer    = textBuf;
            dl.PageAddresses = pageAddrs;
            mPos += 4 + textBuf.Length;
        }
    }

    // ── Compute layout for selection dialog ───────────────────────────────────
    static void ComputeSelectionLayout(P3RSelectionDialog sel, int dialogAddr,
                                        DialogLayout dl, List<int> relocs, ref int mPos)
    {
        int optCount = sel.Options.Length;
        mPos += 24 + 2 + 2 + 2 + 2;  // Name(24) + Field18 + OptionCount + Field1C + Field1E

        int[] optAddrs = new int[optCount];
        if (optCount > 0)
        {
            int textBufOffset = 0x20 + optCount * 4 + 4;  // relative to dialog block start
            int accumulate    = textBufOffset;

            for (int p = 0; p < optCount; p++)
            {
                relocs.Add(mPos);
                optAddrs[p] = accumulate + dialogAddr;
                mPos += 4;
                var ob = sel.Options[p].ToBytes();
                accumulate += ob.Length + 1;  // +1 for null terminator
            }
        }

        var textBuf = BuildSelectionTextBuffer(sel.Options);
        dl.TextBuffer   = textBuf;
        dl.PageAddresses = optAddrs;

        mPos += 4 + textBuf.Length;
    }

    // ── Write message dialog to buffer ────────────────────────────────────────
    static void WriteMessageDialog(byte[] buf, DialogLayout dl, P3RMessageDialog msg)
    {
        int pos = dl.AbsoluteOffset;
        WriteFixedString(buf, pos, msg.Name, 24);
        WriteI16(buf, pos + 24, (short)msg.Pages.Length);
        WriteU16(buf, pos + 26, msg.SpeakerId);
        pos += 28;

        if (msg.Pages.Length > 0)
        {
            for (int i = 0; i < msg.Pages.Length; i++)
            {
                WriteI32(buf, pos, dl.PageAddresses[i]);
                pos += 4;
            }
            WriteI32(buf, pos, dl.TextBuffer.Length);
            dl.TextBuffer.CopyTo(buf, pos + 4);
        }
    }

    // ── Write selection dialog to buffer ─────────────────────────────────────
    static void WriteSelectionDialog(byte[] buf, DialogLayout dl, P3RSelectionDialog sel)
    {
        int pos = dl.AbsoluteOffset;
        WriteFixedString(buf, pos, sel.Name, 24);
        WriteI16(buf, pos + 24, sel.Field18);
        WriteI16(buf, pos + 26, (short)sel.Options.Length);
        WriteI16(buf, pos + 28, sel.Field1C);
        WriteI16(buf, pos + 30, sel.Field1E);
        pos += 32;

        for (int i = 0; i < sel.Options.Length; i++)
        {
            WriteI32(buf, pos, dl.PageAddresses[i]);
            pos += 4;
        }

        WriteI32(buf, pos, dl.TextBuffer.Length);
        dl.TextBuffer.CopyTo(buf, pos + 4);
    }

    // ── Text buffer builders ──────────────────────────────────────────────────
    static byte[] BuildMessageTextBuffer(P3RPage[] pages)
    {
        var buf = new List<byte>();
        foreach (var p in pages)
            buf.AddRange(p.ToBytes());
        buf.Add(0);  // trailing null
        return buf.ToArray();
    }

    static byte[] BuildSelectionTextBuffer(P3RPage[] options)
    {
        var buf = new List<byte>();
        foreach (var o in options)
        {
            buf.AddRange(o.ToBytes());
            buf.Add(0);  // per-option null terminator
        }
        buf.Add(0);  // overall trailing null
        return buf.ToArray();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static int Align4(int v) => (v + 3) & ~3;

    static void WriteI32(byte[] b, int o, int v)   => BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(o, 4), v);
    static void WriteI16(byte[] b, int o, short v) => BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(o, 2), v);
    static void WriteU16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o, 2), v);

    static void WriteFixedString(byte[] buf, int offset, string s, int length)
    {
        var encoded = Encoding.UTF8.GetBytes(s);
        int copyLen = Math.Min(encoded.Length, length);
        Array.Clear(buf, offset, length);  // zero-pad
        encoded.AsSpan(0, copyLen).CopyTo(buf.AsSpan(offset, copyLen));
    }

    // ── Layout scratch-pad ────────────────────────────────────────────────────
    class DialogLayout
    {
        public int  AbsoluteOffset;
        public int  DialogAddress;   // relative to content-base
        public int  NameLength;
        public int  HeaderFixed;
        public int[] PageAddresses = Array.Empty<int>();
        public byte[] TextBuffer  = Array.Empty<byte>();
    }
}
