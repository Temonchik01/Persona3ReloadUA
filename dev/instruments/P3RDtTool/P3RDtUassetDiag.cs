using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace P3RDtTool;

public static class P3RDtUassetDiag
{
    public static void Dump(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        Console.WriteLine($"=== {Path.GetFileName(path)}  ({data.Length} bytes) ===\n");
        Console.WriteLine("HEADER");
        var hdr = new int[16];
        for (int i = 0; i < 16; i++) hdr[i] = RI32(data, i * 4);
        for (int i = 0; i < 16; i++)
            Console.WriteLine($"  [h{i:D2}] 0x{i*4:X2}: {hdr[i],10}  0x{hdr[i]:X8}");
        int nameOffset = hdr[6];
        Console.WriteLine("\nNAME TABLE");
        var names = ParseNames(data, nameOffset, out int nameTableEnd);
        int noneIdx  = names.IndexOf("None");
        Console.WriteLine($"  {names.Count} names. table ends 0x{nameTableEnd:X4}");
        Console.WriteLine($"  Index of \"None\" string = {noneIdx},  Index 0 = \"{names[0]}\" (structural None)");
        for (int i = 0; i < names.Count; i++)
            Console.WriteLine($"  [{i,3}] {names[i]}");
        Console.WriteLine($"\nALL VALID FName SEQUENCES (post 0x{nameTableEnd:X4}, aligned to 4 bytes)");
        Console.WriteLine("  (showing runs of >=2 consecutive valid FName fields at 8-byte FName stride)");
        for (int startMod = 0; startMod < 8; startMod += 4)
        {
            int first = nameTableEnd;
            while ((first % 8) != startMod) first++;
            Console.WriteLine($"\n  --- 8-byte alignment starting at mod-8 offset {startMod} ---");
            var run = new List<(int off, int ni, int num)>();
            for (int pos = first; pos + 7 < data.Length; pos += 8)
            {
                int ni  = RI32(data, pos);
                int num = RI32(data, pos + 4);
                bool valid = ni >= 0 && ni < names.Count;
                if (valid)
                    run.Add((pos, ni, num));
                else if (run.Count >= 2)
                {
                    PrintRun(run, names);
                    run.Clear();
                }
                else run.Clear();
            }
            if (run.Count >= 2) PrintRun(run, names);
        }
        Console.WriteLine("\nSECTION PROBES");
        var offsets = new SortedSet<int>();
        foreach (int v in hdr) if (v > nameTableEnd && v < data.Length) offsets.Add(v);
        foreach (int off in offsets)
        {
            int len = Math.Min(64, data.Length - off);
            Console.Write($"  0x{off:X4}({off}): ");
            for (int i = 0; i < len; i += 4)
            {
                if (i > 0 && i % 32 == 0) Console.Write("\n          ");
                int v = RI32(data, off + i);
                string fname = (v >= 0 && v < names.Count) ? $"[{v}={names[v]}]" : $"0x{v:X8}";
                Console.Write($" {fname}");
            }
            Console.WriteLine();
        }
        Console.WriteLine("\nRAW DUMP 0x0260..END (4-byte aligned, hex + int32 + float)");
        for (int off = 0x0260; off + 3 < data.Length; off += 4)
        {
            int i32 = RI32(data, off);
            float f32 = BitConverter.ToSingle(data, off);
            string fname = (i32 >= 0 && i32 < names.Count) ? $"FN={names[i32]}" : "";
            Console.WriteLine($"  0x{off:X4}: {data[off]:X2} {data[off+1]:X2} {data[off+2]:X2} {data[off+3]:X2}  i32={i32,12}  f32={f32,12:G6}  {fname}");
        }
        Console.WriteLine("\nFLOAT VALUE SEARCH");
        var searchFloats = new (float v, string label)[] { (400f,"400.0"), (40f,"40.0"), (1f,"1.0"), (10f,"10.0"), (0.5f,"0.5") };
        foreach (var (fv, flabel) in searchFloats)
        {
            byte[] fp = BitConverter.GetBytes(fv);
            for (int pos = nameTableEnd; pos <= data.Length - 4; pos++)
            {
                if (data[pos]==fp[0] && data[pos+1]==fp[1] && data[pos+2]==fp[2] && data[pos+3]==fp[3])
                    Console.WriteLine($"  {flabel} @0x{pos:X4}: ctx = {Hex(data, Math.Max(0,pos-16), 28)}");
            }
        }
        Console.WriteLine("\nDATATABLE PARSE ATTEMPTS");
        var noneVariants = new HashSet<int> { 0, noneIdx };
        foreach (int none in noneVariants)
        {
            Console.WriteLine($"\n  Using None = name[{none}] = \"{names[none]}\"");
            for (int pos = nameTableEnd; pos <= data.Length - 8; pos += 4)
            {
                if (RI32(data, pos) != none || RI32(data, pos + 4) != 0) continue;
                if (pos + 12 > data.Length) continue;
                int numEntries = RI32(data, pos + 8);
                if (numEntries < 1 || numEntries > 1000) continue;

                Console.WriteLine($"    None@0x{pos:X4} -> numEntries={numEntries}");
                TryParseRows(data, pos + 12, numEntries, names, none, hasRowKey: true,  "  WITH key");
                TryParseRows(data, pos + 12, numEntries, names, none, hasRowKey: false, "  NO  key");
            }
        }
    }

    static void TryParseRows(byte[] data, int dataStart, int numEntries, List<string> names, int noneIdx, bool hasRowKey, string label)
    {
        int pos = dataStart;
        bool ok = true;
        int totalProps = 0;

        for (int r = 0; r < numEntries && ok; r++)
        {
            string rowKey = "(no key)";
            if (hasRowKey)
            {
                if (pos + 8 > data.Length) { ok = false; break; }
                int kni = RI32(data, pos), knum = RI32(data, pos + 4);
                rowKey = (kni >= 0 && kni < names.Count) ? $"{names[kni]}#{knum}" : $"?{kni}#{knum}";
                pos += 8;
            }
            int props = 0;
            while (pos + 8 <= data.Length && props < 50)
            {
                int pni  = RI32(data, pos);
                int pnum = RI32(data, pos + 4);
                if (pni == noneIdx && pnum == 0) { pos += 8; break; }
                if (pni < 0 || pni >= names.Count) { ok = false; break; }
                if (pos + 28 > data.Length) { ok = false; break; }
                int tni   = RI32(data, pos + 8);
                int tnum  = RI32(data, pos + 16);
                long sz   = RL64(data, pos + 16);
                int  aIdx = RI32(data, pos + 24);

                string pname = names[pni];
                string ptype = (tni >= 0 && tni < names.Count) ? names[tni] : $"?{tni}";

                if (r < 3 && props < 6)
                    Console.WriteLine($"      r{r} prop[{props}] @0x{pos:X4}: name={pname}#{pnum} type={ptype} sz={sz} aIdx={aIdx}");

                pos += 28;
                if (ptype is "StructProperty")  pos += 24;
                else if (ptype is "ObjectProperty" or "WeakObjectProperty") pos += 8;
                else if (ptype is "EnumProperty")  pos += 16;
                else if (ptype is "ByteProperty")  pos += 8;
                else if (ptype is "BoolProperty")  pos += 1;
                else if (ptype is "ArrayProperty" or "SetProperty" or "MapProperty") pos += 8;

                if (sz < 0 || sz > 65536) { ok = false; break; }
                pos += (int)sz;
                props++;
                totalProps++;
            }
        }
        if (ok)
            Console.WriteLine($"      {label}: PARSE OK, ended at 0x{pos:X4}, {totalProps} total props");
    }

    static void PrintRun(List<(int off, int ni, int num)> run, List<string> names)
    {
        Console.Write($"    0x{run[0].off:X4}: ");
        foreach (var (off, ni, num) in run)
            Console.Write($"[{ni}={names[ni]}{(num != 0 ? $"#{num}" : "")}] ");
        Console.WriteLine();
    }
    static List<string> ParseNames(byte[] data, int nameOffset, out int tableEnd)
    {
        var names = new List<string>();
        names.Add("");
        int pos = nameOffset + 1;
        while (pos < data.Length)
        {
            byte len = data[pos];
            if (len == 0) { pos++; break; }
            if (len > 200 || pos + 1 + len >= data.Length) break;
            names.Add(Encoding.UTF8.GetString(data, pos + 1, len));
            pos += 1 + len + 1;
        }
        tableEnd = pos;
        return names;
    }

    static int  RI32(byte[] d, int o) => o + 3 < d.Length ? BitConverter.ToInt32(d, o) : 0;
    static long RL64(byte[] d, int o) => o + 7 < d.Length ? BitConverter.ToInt64(d, o) : 0;
    static string Hex(byte[] d, int off, int count)
    {
        if (off < 0) { count += off; off = 0; }
        count = Math.Min(count, d.Length - off);
        if (count <= 0) return "";
        return string.Join(" ", d[off..(off + count)].Select(b => b.ToString("X2")));
    }
}
