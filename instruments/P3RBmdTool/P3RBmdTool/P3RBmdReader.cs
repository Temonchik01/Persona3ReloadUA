using System.Buffers.Binary;
using System.Text;

namespace P3RBmdTool;

// ── P3R BMD binary reader ──────────────────────────────────────────────────────
//
// Layout (all multi-byte values big-endian after detecting "1GSM" magic):
//
//   0x00  BinaryHeader (32 bytes)
//   0x20  BinaryDialogHeader[DialogCount]  (8 bytes each)
//   0x20 + DialogCount*8  SpeakerTableHeader (16 bytes)
//   <variable>  Dialog data blocks (sought to by Dialog.Offset + 0x20)
//   <variable>  Speaker name offset array + name strings
//   <variable>  Relocation table
//
// Content-base = 0x20 (BinaryHeader.SIZE).
// All "Offset" values are relative to content-base, EXCEPT
//   RelocationTable.Offset which is absolute.

public static class P3RBmdReader
{
    const int HDR = 32;   // BinaryHeader.SIZE

    public static P3RBmdFile Read(byte[] data)
    {
        if (data.Length < HDR)
            throw new InvalidDataException("File too small to be a P3R BMD.");

        // Verify magic "1GSM" at offset 8
        if (data[8] != 0x31 || data[9] != 0x47 || data[10] != 0x53 || data[11] != 0x4D)
            throw new InvalidDataException("Not a P3R BMD (missing 1GSM magic).");

        var bmd = new P3RBmdFile();

        // ── Read header ──────────────────────────────────────────────────────
        // byte 0: FileType (always 7, ignored on read)
        bmd.IsCompressed = data[1] != 0;
        bmd.UserId       = ReadI16(data, 2);
        int fileSize     = ReadI32(data, 4);   // for bounds checking
        // magic at 8-11: "1GSM"
        bmd.Field0C      = ReadI32(data, 12);
        // relocation table offset (absolute) at 16 — decoded but not needed for reading
        // relocation table size at 20 — same
        int dialogCount  = ReadI32(data, 24);
        // IsRelocated at 28 (int16)
        bmd.Field1E      = ReadI16(data, 30);

        // ── Read dialog headers ──────────────────────────────────────────────
        int pos = HDR;
        var dialogs = new List<P3RDialog>(dialogCount);
        var dialogHeaderOffsets = new (P3RDialogKind kind, int offset)[dialogCount];

        for (int i = 0; i < dialogCount; i++)
        {
            var kind   = (P3RDialogKind)ReadI32(data, pos);
            int offset = ReadI32(data, pos + 4);
            dialogHeaderOffsets[i] = (kind, offset);
            pos += 8;
        }

        // ── Read speaker table header (always follows dialog headers) ────────
        int speakerNameArrayOffset = ReadI32(data, pos);
        int speakerCount           = ReadI32(data, pos + 4);
        int speakerField08         = ReadI32(data, pos + 8);
        int speakerField0C         = ReadI32(data, pos + 12);
        pos += 16;

        var speakers = new P3RSpeakerTable
        {
            Field08 = speakerField08,
            Field0C = speakerField0C,
        };

        if (speakerCount > 0 && speakerNameArrayOffset != 0)
            speakers.Names = ReadSpeakerNames(data, speakerNameArrayOffset, speakerCount);

        bmd.Speakers = speakers;

        // ── Read each dialog ─────────────────────────────────────────────────
        foreach (var (kind, offset) in dialogHeaderOffsets)
        {
            int abs = HDR + offset;   // absolute byte position of dialog data
            dialogs.Add(kind switch
            {
                P3RDialogKind.Message   => ReadMessageDialog(data, abs),
                P3RDialogKind.Selection => ReadSelectionDialog(data, abs),
                _ => throw new InvalidDataException($"Unknown dialog kind: {kind}")
            });
        }

        bmd.Dialogs = dialogs.ToArray();
        return bmd;
    }

    // ── Message dialog ────────────────────────────────────────────────────────
    static P3RMessageDialog ReadMessageDialog(byte[] data, int abs)
    {
        var d = new P3RMessageDialog();
        d.Name      = ReadFixedString(data, abs, 24);
        short pageCount = ReadI16(data, abs + 24);   // Name(24) PageCount(2) SpeakerId(2)
        d.SpeakerId = ReadU16(data, abs + 26);

        if (pageCount <= 0)
            return d;

        // PageStartAddresses[pageCount] at abs+0x1C = abs+28
        int addrBase = abs + 28;
        var pageAddrs = new int[pageCount];
        for (int i = 0; i < pageCount; i++)
            pageAddrs[i] = ReadI32(data, addrBase + i * 4);

        int textBufSizeOff = addrBase + pageCount * 4;
        int textBufSize    = ReadI32(data, textBufSizeOff);
        int textBufStart   = textBufSizeOff + 4;

        // Decode pages.
        // PageStartAddresses are absolute relative to content-base (HDR=32).
        // The text buffer starts at pageAddrs[0]-HDR from start of file in content space.
        // i.e. byte position in file = HDR + pageAddrs[0]
        // which equals textBufStart.  We'll just use textBufStart as the base.
        d.Pages = DecodePages(data, textBufStart, textBufSize, pageAddrs, isSelection: false);
        return d;
    }

    // ── Selection dialog ──────────────────────────────────────────────────────
    static P3RSelectionDialog ReadSelectionDialog(byte[] data, int abs)
    {
        var d = new P3RSelectionDialog();
        d.Name    = ReadFixedString(data, abs, 24);
        d.Field18 = ReadI16(data, abs + 24);
        short optCount = ReadI16(data, abs + 26);
        d.Field1C = ReadI16(data, abs + 28);
        d.Field1E = ReadI16(data, abs + 30);

        if (optCount <= 0)
            return d;

        // OptionStartAddresses at abs+0x20 = abs+32
        int addrBase = abs + 32;
        var optAddrs = new int[optCount];
        for (int i = 0; i < optCount; i++)
            optAddrs[i] = ReadI32(data, addrBase + i * 4);

        int textBufSizeOff = addrBase + optCount * 4;
        int textBufSize    = ReadI32(data, textBufSizeOff);
        int textBufStart   = textBufSizeOff + 4;

        d.Options = DecodePages(data, textBufStart, textBufSize, optAddrs, isSelection: true);
        return d;
    }

    // ── Page decoding ─────────────────────────────────────────────────────────
    //
    // TextBuffer layout:
    //   Message:   pages are separated by the addresses; trailing 0x00 after last page.
    //   Selection: each option is null-terminated; extra trailing 0x00.
    //
    // pageAddrs[i] is the ABSOLUTE offset from content base (HDR).
    // textBufStart is the absolute file offset of the text buffer.
    // The offset of page i within the text buffer =
    //     (pageAddrs[i] - HDR) - (pageAddrs[0] - HDR)  = pageAddrs[i] - pageAddrs[0]
    //
    static P3RPage[] DecodePages(byte[] data, int textBufStart, int textBufSize,
                                  int[] pageAddrs, bool isSelection)
    {
        if (textBufSize <= 0 || pageAddrs.Length == 0)
            return Array.Empty<P3RPage>();

        int baseAddr = pageAddrs[0];   // all addrs relative to this

        var pages = new P3RPage[pageAddrs.Length];
        for (int i = 0; i < pageAddrs.Length; i++)
        {
            int startInBuf = pageAddrs[i] - baseAddr;
            int endInBuf;

            if (i + 1 < pageAddrs.Length)
                endInBuf = pageAddrs[i + 1] - baseAddr;
            else
                endInBuf = textBufSize - 1;  // exclude final trailing null

            // Clamp to buffer
            startInBuf = Math.Max(0, Math.Min(startInBuf, textBufSize));
            endInBuf   = Math.Max(startInBuf, Math.Min(endInBuf, textBufSize));

            int pageAbsStart = textBufStart + startInBuf;
            int pageLen      = endInBuf - startInBuf;

            // For selection options: null-terminated, so stop at first 0x00
            if (isSelection)
            {
                for (int j = 0; j < pageLen; j++)
                {
                    if (data[pageAbsStart + j] == 0x00) { pageLen = j; break; }
                }
            }

            var pageBytes = data.AsSpan(pageAbsStart, pageLen).ToArray();
            pages[i] = new P3RPage { Tokens = ParseTokens(pageBytes) };
        }

        return pages;
    }

    // ── Token parser ──────────────────────────────────────────────────────────
    //
    // Byte categories in a page:
    //   0x00       null (end — should not appear inside a page slice)
    //   0x0A       newline
    //   0x0D       carriage return (preserved verbatim)
    //   0xFE       function call prefix → read signifier, funcId, argBytes
    //   0xF0-0xFF  (other high bytes, rare; treated as raw if not 0xFE)
    //   0x80-0xEF  UTF-8 multi-byte sequences (Japanese, etc.)
    //   0x01-0x7F  ASCII / single-byte
    //
    public static P3RToken[] ParseTokens(byte[] bytes)
    {
        var tokens = new List<P3RToken>();
        var textBuf = new StringBuilder();

        void FlushText()
        {
            if (textBuf.Length > 0)
            {
                tokens.Add(new P3RTextToken(textBuf.ToString()));
                textBuf.Clear();
            }
        }

        int i = 0;
        while (i < bytes.Length)
        {
            byte b = bytes[i];

            if (b == 0x00)
            {
                break;  // null terminator
            }
            else if (b == 0x0A)
            {
                FlushText();
                tokens.Add(new P3RNewlineToken());
                i++;
            }
            else if (b == 0x0D)
            {
                FlushText();
                tokens.Add(new P3RCrToken());
                i++;
            }
            else if (b == 0xFE)
            {
                // Function call:  FE <signifier> <funcId> <argBytes...>
                if (i + 2 >= bytes.Length)
                {
                    // Truncated — emit as raw
                    FlushText();
                    for (; i < bytes.Length; i++)
                        tokens.Add(new P3RRawByteToken(bytes[i]));
                    break;
                }

                FlushText();
                byte signifier = bytes[i + 1];
                byte funcId    = bytes[i + 2];
                int argCount   = (signifier & 0x0F) - 1;
                if (argCount < 0) argCount = 0;

                int argByteCount = argCount * 2;
                int endIdx = i + 3 + argByteCount;
                if (endIdx > bytes.Length) endIdx = bytes.Length;

                var argBytes = bytes.AsSpan(i + 3, Math.Max(0, endIdx - (i + 3))).ToArray();
                tokens.Add(new P3RFunctionToken(signifier, funcId, argBytes));
                i = endIdx;
            }
            else if (b >= 0xF0)
            {
                // Unknown high byte — preserve as raw
                FlushText();
                tokens.Add(new P3RRawByteToken(b));
                i++;
            }
            else if (b >= 0x80)
            {
                // UTF-8 multi-byte: determine length from high bits
                int seqLen = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
                seqLen = Math.Min(seqLen, bytes.Length - i);
                try
                {
                    string ch = Encoding.UTF8.GetString(bytes, i, seqLen);
                    textBuf.Append(ch);
                }
                catch
                {
                    textBuf.Append('?');
                }
                i += seqLen;
            }
            else
            {
                // ASCII / single byte
                textBuf.Append((char)b);
                i++;
            }
        }

        FlushText();
        return tokens.ToArray();
    }

    // ── Speaker names ─────────────────────────────────────────────────────────
    static P3RSpeakerName[] ReadSpeakerNames(byte[] data, int arrayOffset, int count)
    {
        int abs = HDR + arrayOffset;
        var names = new P3RSpeakerName[count];

        for (int i = 0; i < count; i++)
        {
            if (abs + i * 4 + 4 > data.Length) break;
            int nameOffset = ReadI32(data, abs + i * 4);
            int nameAbs    = HDR + nameOffset;
            var nameBytes  = ReadNullTerminatedBytes(data, nameAbs);
            names[i] = new P3RSpeakerName(nameBytes);
        }

        return names;
    }

    static byte[] ReadNullTerminatedBytes(byte[] data, int start)
    {
        int end = start;
        while (end < data.Length && data[end] != 0) end++;
        return data.AsSpan(start, end - start).ToArray();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static int    ReadI32(byte[] d, int o) => BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(o, 4));
    static short  ReadI16(byte[] d, int o) => BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(o, 2));
    static ushort ReadU16(byte[] d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o, 2));

    static string ReadFixedString(byte[] d, int offset, int length)
    {
        // Find first null byte within the fixed-length field
        int end = offset;
        int max = offset + length;
        while (end < max && d[end] != 0) end++;
        return Encoding.UTF8.GetString(d, offset, end - offset);
    }
}
