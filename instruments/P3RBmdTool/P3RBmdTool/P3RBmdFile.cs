using System.Text;

namespace P3RBmdTool;

// ── Data model for P3R BMD format (BinaryFormatVersion.Version1BigEndian) ────

public enum P3RDialogKind { Message = 0, Selection = 1 }

public class P3RBmdFile
{
    // Header fields preserved for round-trip (FileSize / RelTableOffset rebuilt on write)
    public bool   IsCompressed;
    public short  UserId;
    public int    Field0C;
    public short  Field1E;  // always 2

    public P3RDialog[]     Dialogs  = Array.Empty<P3RDialog>();
    public P3RSpeakerTable Speakers = new();
}

public class P3RSpeakerTable
{
    public int             Field08;
    public int             Field0C;
    public P3RSpeakerName[] Names = Array.Empty<P3RSpeakerName>();
}

public class P3RSpeakerName
{
    public byte[] RawBytes;  // UTF-8 bytes (null terminator NOT included)
    public string Text => Encoding.UTF8.GetString(RawBytes);
    public P3RSpeakerName(byte[] raw) => RawBytes = raw;
}

// ── Dialogs ───────────────────────────────────────────────────────────────────

public abstract class P3RDialog
{
    public P3RDialogKind Kind;
    public string Name = "";   // up to 23 chars + null padding to 24 bytes
}

public class P3RMessageDialog : P3RDialog
{
    public ushort    SpeakerId;  // 0xFFFF = none, 0x8000|n = variable, n = named
    public P3RPage[] Pages = Array.Empty<P3RPage>();

    public P3RMessageDialog() => Kind = P3RDialogKind.Message;
}

public class P3RSelectionDialog : P3RDialog
{
    public short     Field18;
    public short     Field1C;
    public short     Field1E;
    public P3RPage[] Options = Array.Empty<P3RPage>();  // each option is one "page"

    public P3RSelectionDialog() => Kind = P3RDialogKind.Selection;
}

// ── Pages / tokens ────────────────────────────────────────────────────────────

// A page (or selection option) is a sequence of tokens.
public class P3RPage
{
    public P3RToken[] Tokens = Array.Empty<P3RToken>();

    // Serialize page back to raw bytes (not including trailing null — that's the writer's job)
    public byte[] ToBytes()
    {
        var buf = new List<byte>();
        foreach (var tok in Tokens)
            tok.AppendBytes(buf);
        return buf.ToArray();
    }
}

public abstract class P3RToken
{
    public abstract void AppendBytes(List<byte> buf);
}

// Plain UTF-8 text segment
public class P3RTextToken : P3RToken
{
    public string Text;
    public P3RTextToken(string text) => Text = text;

    public override void AppendBytes(List<byte> buf) =>
        buf.AddRange(Encoding.UTF8.GetBytes(Text));
}

// 0x0A newline
public class P3RNewlineToken : P3RToken
{
    public override void AppendBytes(List<byte> buf) => buf.Add(0x0A);
}

// 0x0D carriage return (some files have CRLF — preserved verbatim)
public class P3RCrToken : P3RToken
{
    public override void AppendBytes(List<byte> buf) => buf.Add(0x0D);
}

// FE <signifier> <functionId> <rawArgBytes...>
public class P3RFunctionToken : P3RToken
{
    public byte   Signifier;   // 0xF1-0xFF  (0xF0 | (argCount+1))
    public byte   FunctionId;  // (tableIdx<<5) | funcIdx
    public byte[] RawArgBytes; // raw bytes — preserved verbatim, not re-encoded

    public int TableIndex => (FunctionId & 0xE0) >> 5;
    public int FuncIndex  => FunctionId & 0x1F;
    public int ArgCount   => (Signifier & 0x0F) - 1;  // number of 2-byte arg pairs

    // Decode a stored arg pair (low, high) back to int16 value.
    // Encoding: firstByte = (value & 0xFF) + 1, secondByte = ((value>>8)&0xFF) + 1
    // Special: secondByte 0xFF means high = 0 (not decremented).
    public short GetArgValue(int index)
    {
        if (index < 0 || index >= ArgCount) return 0;
        byte lo = RawArgBytes[index * 2];
        byte hi = RawArgBytes[index * 2 + 1];
        byte decodedLo = (byte)(lo - 1);
        byte decodedHi = hi != 0xFF ? (byte)((hi - 1) & 0xFF) : (byte)0;
        return (short)(decodedLo | (decodedHi << 8));
    }

    public P3RFunctionToken(byte signifier, byte functionId, byte[] rawArgBytes)
    {
        Signifier    = signifier;
        FunctionId   = functionId;
        RawArgBytes  = rawArgBytes;
    }

    public override void AppendBytes(List<byte> buf)
    {
        buf.Add(0xFE);
        buf.Add(Signifier);
        buf.Add(FunctionId);
        buf.AddRange(RawArgBytes);
    }
}

// Any unrecognised single byte (defensive fallback)
public class P3RRawByteToken : P3RToken
{
    public byte Value;
    public P3RRawByteToken(byte v) => Value = v;
    public override void AppendBytes(List<byte> buf) => buf.Add(Value);
}
