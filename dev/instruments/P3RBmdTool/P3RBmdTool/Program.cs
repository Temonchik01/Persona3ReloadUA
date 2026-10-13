// P3RBmdTool
//
// Drag-and-drop:
//   Drop a .uasset  → extracts raw BMD → saves .bmd next to it
//   Drop a .bmd     → exports to .xml next to it
//   Drop a .xml     → imports XML → saves .bmd next to the xml
//
// Command-line (uasset ↔ bmd):
//   --unpack    <file.uasset>
//   --pack      <file.bmd>  <original.uasset>
//   --roundtrip <file.uasset>
//   --roundtrip-all [dir]
//   --extract-all   [dir] [outdir]
//
// Command-line (batch testing):
//   --batch-test    [dir]   reads every .bmd, rebuilds binary, reports accuracy
//   --batch-xml     [dir]   export→import XML every .bmd, reports accuracy
//   --batch-xliff   [dir]   export→import XLIFF every .bmd, reports accuracy
//
// Command-line (bmd ↔ xml):
//   --export-xml  <file.bmd>  [output.xml]
//   --import-xml  <file.xml>  [output.bmd]     (rebuilds bmd from xml)
//   --dump        <file.bmd>                   (print structure to console)
//   --bmd-roundtrip <file.bmd>                 (read→write, byte-compare)
//
// Command-line (batch xml — main workflow):
//   --batch-export-xml-uasset   <uasset-src-dir> <xml-out-dir>   (mirrors uasset tree)
//   --batch-import-xml          <xml-dir> <uasset-src-dir> <mod-assets-dir>
//
// Command-line (bmd ↔ xliff):
//   --export-xliff              <file.bmd>  [output.xliff]
//   --import-xliff              <file.xliff>  [output.bmd]
//   --batch-export-xliff        <bmd-dir>  <xliff-out-dir>
//   --batch-export-xliff-uasset <uasset-src-dir> <xliff-out-dir>   (mirrors uasset tree)
//   --batch-import-xliff        <xliff-dir> <uasset-src-dir> <mod-assets-dir>
//
// Mod folder management:
//   --make-mod <uasset-src-dir> <mod-dir>   copies English uassets + creates ModConfig.json
//
// Full pipeline:
//   --full-export <file.uasset>   → extracts .bmd AND exports .xml
//   --full-import <file.xml>      → imports .xml → .bmd → repacks .uasset

using P3RBmdTool;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Drop .uasset  → extract .bmd");
    Console.Error.WriteLine("  Drop .bmd     → export .xml");
    Console.Error.WriteLine("  Drop .xml     → import to .bmd");
    Console.Error.WriteLine("  --unpack    <file.uasset>");
    Console.Error.WriteLine("  --pack      <file.bmd> <original.uasset>");
    Console.Error.WriteLine("  --export-xml  <file.bmd> [output.xml]");
    Console.Error.WriteLine("  --import-xml  <file.xml> [output.bmd]");
    Console.Error.WriteLine("  --dump        <file.bmd>");
    Console.Error.WriteLine("  --bmd-roundtrip <file.bmd>");
    Console.Error.WriteLine("  --full-export <file.uasset>");
    Console.Error.WriteLine("  --full-import <file.xml>");
    Console.Error.WriteLine("  --roundtrip <file.uasset>");
    Console.Error.WriteLine("  --roundtrip-all [dir]");
    Console.Error.WriteLine("  --extract-all [dir] [outdir]");
    PauseOnError();
    return;
}

switch (args[0])
{
    case "--unpack":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        Unpack(args[1]);
        break;

    case "--pack":
        if (args.Length < 3) { Console.Error.WriteLine("Usage: --pack <bmd> <uasset>"); PauseOnError(); return; }
        Pack(args[1], args[2]);
        break;

    case "--export-xml":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        ExportXml(args[1], args.Length > 2 ? args[2] : null);
        break;

    case "--import-xml":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        ImportXml(args[1], args.Length > 2 ? args[2] : null);
        break;

    case "--dump":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        Dump(args[1]);
        break;

    case "--bmd-roundtrip":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        BmdRoundTrip(args[1]);
        break;

    case "--batch-test":
        BatchTest(args.Length > 1 ? args[1] : ".", xml: false);
        break;

    case "--batch-xml":
        BatchTest(args.Length > 1 ? args[1] : ".", xml: true);
        break;

    case "--batch-xliff":
        BatchXliff(args.Length > 1 ? args[1] : ".");
        break;

    case "--batch-export-xml-uasset":
        if (args.Length < 3) { Console.Error.WriteLine("Usage: --batch-export-xml-uasset <uasset-src-dir> <xml-out-dir>"); PauseOnError(); return; }
        BatchExportXmlUasset(args[1], args[2]);
        break;

    case "--batch-import-xml":
        if (args.Length < 4) { Console.Error.WriteLine("Usage: --batch-import-xml <xml-dir> <uasset-src-dir> <mod-assets-dir>"); PauseOnError(); return; }
        BatchImportXml(args[1], args[2], args[3], force: args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase)));
        break;

    case "--batch-roundtrip-xml-uasset":
        if (args.Length < 3) { Console.Error.WriteLine("Usage: --batch-roundtrip-xml-uasset <xml-dir> <uasset-src-dir>"); PauseOnError(); return; }
        BatchRoundtripXmlUasset(args[1], args[2]);
        break;

    case "--batch-export-xliff":
        if (args.Length < 3) { Console.Error.WriteLine("Usage: --batch-export-xliff <bmd-dir> <xliff-out-dir>"); PauseOnError(); return; }
        BatchExportXliff(args[1], args[2]);
        break;

    case "--batch-export-xliff-uasset":
        if (args.Length < 3) { Console.Error.WriteLine("Usage: --batch-export-xliff-uasset <uasset-src-dir> <xliff-out-dir>"); PauseOnError(); return; }
        BatchExportXliffUasset(args[1], args[2]);
        break;

    case "--batch-import-xliff":
        if (args.Length < 4) { Console.Error.WriteLine("Usage: --batch-import-xliff <xliff-dir> <uasset-src-dir> <mod-assets-dir>"); PauseOnError(); return; }
        BatchImportXliff(args[1], args[2], args[3]);
        break;

    case "--diag-uasset":
        if (args.Length < 2) { Console.Error.WriteLine("Usage: --diag-uasset <file.uasset>"); PauseOnError(); return; }
        DiagUasset(args[1]);
        break;

    case "--check-reloc":
        if (args.Length < 2) { Console.Error.WriteLine("Usage: --check-reloc <file-or-dir.uasset>"); PauseOnError(); return; }
        if (Directory.Exists(args[1]))
        {
            int crOk = 0, crBad = 0;
            var crFiles = Directory.EnumerateFiles(args[1], "BMD_*.uasset", SearchOption.AllDirectories).OrderBy(f => f).ToList();
            Console.WriteLine($"Checking {crFiles.Count} BMD uassets under {args[1]} ...\n");
            foreach (var crFile in crFiles)
            {
                bool crPass = CheckReloc(crFile, quiet: true);
                if (crPass) crOk++; else crBad++;
            }
            Console.WriteLine($"\nBatch reloc check: {crOk} OK, {crBad} MISMATCH");
        }
        else
        {
            CheckReloc(args[1], quiet: false);
        }
        break;

    case "--make-mod":
        if (args.Length < 3) { Console.Error.WriteLine("Usage: --make-mod <uasset-src-dir> <mod-dir>"); PauseOnError(); return; }
        MakeMod(args[1], args[2]);
        break;

    case "--export-xliff":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        ExportXliff(args[1], args.Length > 2 ? args[2] : null);
        break;

    case "--import-xliff":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        ImportXliff(args[1], args.Length > 2 ? args[2] : null);
        break;

    case "--full-export":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        FullExport(args[1]);
        break;

    case "--full-import":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        FullImport(args[1]);
        break;

    case "--roundtrip":
        if (args.Length < 2) { Console.Error.WriteLine("Missing file."); PauseOnError(); return; }
        RoundTrip(args[1]);
        break;

    case "--roundtrip-all":
        RoundTripAll(args.Length > 1 ? args[1] : ".");
        break;

    case "--extract-all":
        ExtractAll(args.Length > 1 ? args[1] : ".", args.Length > 2 ? args[2] : "extracted_bmd");
        break;

    default:
        string dropped = args[0];
        if (dropped.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            Unpack(dropped);
        else if (dropped.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
            ExportXliff(dropped, null);
        else if (dropped.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            DragDropXmlToBmd(dropped);
        else if (dropped.EndsWith(".xliff", StringComparison.OrdinalIgnoreCase))
            ImportXliff(dropped, null);
        else
        {
            Console.Error.WriteLine($"Unknown file type: {dropped}");
            PauseOnError();
        }
        break;
}

// ── BMD ↔ XML ─────────────────────────────────────────────────────────────────
static void ExportXml(string bmdPath, string? outPath)
{
    byte[]? data = TryRead(bmdPath);
    if (data == null) { PauseOnError(); return; }

    P3RBmdFile bmd;
    try { bmd = P3RBmdReader.Read(data); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR reading BMD: {ex.Message}"); PauseOnError(); return; }

    string xml = P3RXmlTool.Export(bmd, bmdPath);
    outPath ??= Path.ChangeExtension(bmdPath, ".xml");
    File.WriteAllText(outPath, xml, System.Text.Encoding.UTF8);
    Console.WriteLine($"{Path.GetFileName(bmdPath)} → {Path.GetFileName(outPath)}  ({bmd.Dialogs.Length} dialogs)");
}

static void ImportXml(string xmlPath, string? outPath)
{
    string xmlText;
    try { xmlText = File.ReadAllText(xmlPath, System.Text.Encoding.UTF8); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR reading XML: {ex.Message}"); PauseOnError(); return; }

    // Find the original BMD for structure reference
    string bmdPath;
    string sourcePath = P3RXmlTool.GetSourcePath(xmlText);
    if (sourcePath.Length > 0 && File.Exists(sourcePath))
        bmdPath = sourcePath;
    else
    {
        bmdPath = Path.ChangeExtension(xmlPath, ".bmd");
        if (!File.Exists(bmdPath))
        {
            Console.Error.WriteLine($"ERROR: cannot find original .bmd for {Path.GetFileName(xmlPath)}");
            Console.Error.WriteLine($"  Looked for: {bmdPath}");
            Console.Error.WriteLine($"  (set sourcePath in the XML, or place a .bmd with the same name)");
            PauseOnError();
            return;
        }
    }

    byte[]? origData = TryRead(bmdPath);
    if (origData == null) { PauseOnError(); return; }

    P3RBmdFile origBmd;
    try { origBmd = P3RBmdReader.Read(origData); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR reading original BMD: {ex.Message}"); PauseOnError(); return; }

    P3RBmdFile newBmd;
    try { newBmd = P3RXmlTool.Apply(origBmd, xmlText); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR applying XML: {ex.Message}"); PauseOnError(); return; }

    byte[] newData;
    try { newData = P3RBmdWriter.Write(newBmd); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR writing BMD: {ex.Message}"); PauseOnError(); return; }

    outPath ??= Path.ChangeExtension(xmlPath, ".bmd");
    File.WriteAllBytes(outPath, newData);
    Console.WriteLine($"{Path.GetFileName(xmlPath)} → {Path.GetFileName(outPath)}  ({newData.Length} bytes, delta={newData.Length - origData.Length:+#;-#;0})");
}

static void DragDropXmlToBmd(string xmlPath)
{
    ImportXml(xmlPath, null);
}

static void Dump(string bmdPath)
{
    byte[]? data = TryRead(bmdPath);
    if (data == null) { PauseOnError(); return; }

    try
    {
        var bmd = P3RBmdReader.Read(data);
        Console.WriteLine($"File: {Path.GetFileName(bmdPath)}  ({data.Length} bytes)");
        P3RXmlTool.DumpInfo(bmd);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        PauseOnError();
    }
}

static void BmdRoundTrip(string bmdPath)
{
    byte[]? orig = TryRead(bmdPath);
    if (orig == null) { PauseOnError(); return; }

    P3RBmdFile bmd;
    try { bmd = P3RBmdReader.Read(orig); }
    catch (Exception ex) { Console.Error.WriteLine($"READ ERROR: {ex.Message}"); PauseOnError(); return; }

    byte[] rebuilt;
    try { rebuilt = P3RBmdWriter.Write(bmd); }
    catch (Exception ex) { Console.Error.WriteLine($"WRITE ERROR: {ex.Message}"); PauseOnError(); return; }

    // Compare content-base area (skip trailing padding bytes beyond FileSize)
    int compareLen = Math.Min(orig.Length, rebuilt.Length);
    int diffs = 0;
    for (int i = 0; i < compareLen; i++)
    {
        if (orig[i] != rebuilt[i])
        {
            if (diffs < 20)
                Console.WriteLine($"  DIFF @ 0x{i:X4}: orig={orig[i]:X2} rebuilt={rebuilt[i]:X2}");
            diffs++;
        }
    }

    if (diffs == 0 && orig.Length == rebuilt.Length)
        Console.WriteLine($"PERFECT MATCH ({orig.Length} bytes)");
    else if (diffs == 0)
        Console.WriteLine($"CONTENT MATCH, sizes differ: orig={orig.Length} rebuilt={rebuilt.Length}");
    else
        Console.WriteLine($"MISMATCH: {diffs} byte diffs  orig={orig.Length} rebuilt={rebuilt.Length}");
}

// ── Full pipeline ─────────────────────────────────────────────────────────────
static void FullExport(string uassetPath)
{
    byte[]? data = TryRead(uassetPath);
    if (data == null) { PauseOnError(); return; }

    int bmdOffset = FindBmdOffset(data);
    if (bmdOffset < 0) { Console.Error.WriteLine("ERROR: no BMD in uasset."); PauseOnError(); return; }

    string bmdPath = Path.ChangeExtension(uassetPath, ".bmd");
    File.WriteAllBytes(bmdPath, data[bmdOffset..]);
    Console.WriteLine($"  Extracted: {Path.GetFileName(bmdPath)}");
    ExportXml(bmdPath, null);
}

static void FullImport(string xmlPath)
{
    string xmlText;
    try { xmlText = File.ReadAllText(xmlPath, System.Text.Encoding.UTF8); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR reading XML: {ex.Message}"); PauseOnError(); return; }

    // ImportXml handles the xml→bmd step
    string bmdOut = Path.ChangeExtension(xmlPath, ".bmd");
    ImportXml(xmlPath, bmdOut);
    if (!File.Exists(bmdOut)) return;

    // Find matching uasset
    string uassetPath = Path.ChangeExtension(xmlPath, ".uasset");
    if (!File.Exists(uassetPath))
    {
        string sourceBmd = P3RXmlTool.GetSourcePath(xmlText);
        if (sourceBmd.Length > 0)
            uassetPath = Path.ChangeExtension(sourceBmd, ".uasset");
    }

    if (!File.Exists(uassetPath))
    {
        Console.WriteLine($"  Skipping repack: no matching .uasset found for {Path.GetFileName(xmlPath)}");
        return;
    }

    Pack(bmdOut, uassetPath);
}

// ── Uasset ↔ BMD ──────────────────────────────────────────────────────────────
static void Unpack(string uassetPath)
{
    byte[]? data = TryRead(uassetPath);
    if (data == null) { PauseOnError(); return; }

    int bmdOffset = FindBmdOffset(data);
    if (bmdOffset < 0)
    {
        Console.Error.WriteLine($"ERROR: 1GSM marker not found in {Path.GetFileName(uassetPath)}");
        PauseOnError();
        return;
    }

    byte[] bmd = data[bmdOffset..];
    string outPath = Path.ChangeExtension(uassetPath, ".bmd");
    File.WriteAllBytes(outPath, bmd);
    Console.WriteLine($"{Path.GetFileName(uassetPath)} → {Path.GetFileName(outPath)}  ({bmd.Length} bytes, header={bmdOffset} bytes)");
}

static void Pack(string bmdPath, string origUassetPath)
{
    byte[]? bmd = TryRead(bmdPath);
    if (bmd == null) { PauseOnError(); return; }

    byte[]? orig = TryRead(origUassetPath);
    if (orig == null) { PauseOnError(); return; }

    int bmdOffset = FindBmdOffset(orig);
    if (bmdOffset < 0)
    {
        Console.Error.WriteLine($"ERROR: 1GSM not found in {Path.GetFileName(origUassetPath)}");
        PauseOnError();
        return;
    }

    // UE4 uassets always have a 12-byte tail after the BMD blob. Preserve it.
    byte[] uassetTail = orig[(orig.Length - 12)..];

    string outPath = Path.ChangeExtension(bmdPath, ".uasset");
    try { RepackUasset(origUassetPath, bmd, outPath); }
    catch (Exception ex) { Console.Error.WriteLine($"REPACK ERROR: {ex.Message}"); PauseOnError(); return; }
    var outInfo = new FileInfo(outPath);
    Console.WriteLine($"{Path.GetFileName(bmdPath)} → {Path.GetFileName(outPath)}  ({outInfo.Length} bytes, delta={outInfo.Length - orig.Length:+#;-#;0})");
}

static void RoundTrip(string uassetPath)
{
    byte[]? orig = TryRead(uassetPath);
    if (orig == null) { PauseOnError(); return; }

    int bmdOffset = FindBmdOffset(orig);
    if (bmdOffset < 0) { Console.Error.WriteLine("ERROR: 1GSM not found."); PauseOnError(); return; }

    byte[] rebuilt = new byte[orig.Length];
    orig[..bmdOffset].CopyTo(rebuilt, 0);
    orig[bmdOffset..].CopyTo(rebuilt, bmdOffset);

    if (orig.SequenceEqual(rebuilt))
        Console.WriteLine($"PERFECT MATCH ({orig.Length} bytes, BMD at +0x{bmdOffset:X})");
    else
    {
        Console.WriteLine($"MISMATCH — orig={orig.Length}  rebuilt={rebuilt.Length}");
        for (int i = 0; i < Math.Max(orig.Length, rebuilt.Length); i++)
        {
            byte o = i < orig.Length    ? orig[i]    : (byte)0;
            byte r = i < rebuilt.Length ? rebuilt[i] : (byte)0;
            if (o != r) Console.WriteLine($"  0x{i:X4}: orig={o:X2} rebuilt={r:X2}");
        }
    }
}

static void ExtractAll(string root, string outDir)
{
    var files = Directory.GetFiles(root, "*.uasset", SearchOption.AllDirectories);
    Directory.CreateDirectory(outDir);
    Console.WriteLine($"Extracting BMDs from {files.Length} uassets → {Path.GetFullPath(outDir)}\n");

    int ok = 0, skip = 0, fail = 0;
    foreach (var path in files)
    {
        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch (Exception ex) { Console.Error.WriteLine($"ERR read {Path.GetFileName(path)}: {ex.Message}"); fail++; continue; }

        int bmdOffset = FindBmdOffset(data);
        if (bmdOffset < 0) { skip++; continue; }

        string outName = Path.GetFileNameWithoutExtension(path) + ".bmd";
        string outPath = Path.Combine(outDir, outName);
        if (File.Exists(outPath))
        {
            int n = 2;
            while (File.Exists(outPath))
                outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(outName) + $"_{n++}.bmd");
        }
        File.WriteAllBytes(outPath, data[bmdOffset..]);
        ok++;
    }

    Console.WriteLine($"Done: {ok} extracted  |  {skip} skipped (no BMD)  |  {fail} errors");
}

static void RoundTripAll(string root)
{
    var files = Directory.GetFiles(root, "*.uasset", SearchOption.AllDirectories);
    Console.WriteLine($"Scanning {files.Length} uasset files under {root} ...\n");

    int ok = 0, fail = 0, skip = 0;
    var failures = new List<(string path, string reason)>();

    foreach (var path in files)
    {
        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch (Exception ex) { failures.Add((path, $"read error: {ex.Message}")); fail++; continue; }

        int bmdOffset = FindBmdOffset(data);
        if (bmdOffset < 0) { skip++; continue; }

        byte[] rebuilt = new byte[data.Length];
        data[..bmdOffset].CopyTo(rebuilt, 0);
        data[bmdOffset..].CopyTo(rebuilt, bmdOffset);

        if (data.SequenceEqual(rebuilt))
            ok++;
        else
        {
            fail++;
            int firstDiff = -1;
            for (int i = 0; i < Math.Max(data.Length, rebuilt.Length); i++)
            {
                byte o = i < data.Length    ? data[i]    : (byte)0;
                byte r = i < rebuilt.Length ? rebuilt[i] : (byte)0;
                if (o != r) { firstDiff = i; break; }
            }
            failures.Add((path, $"MISMATCH orig={data.Length} rebuilt={rebuilt.Length} first diff at 0x{firstDiff:X}"));
        }
    }

    Console.WriteLine($"Results: {ok} OK  |  {fail} FAIL  |  {skip} skipped (no BMD)");
    if (failures.Count > 0)
    {
        Console.WriteLine("\nFailures:");
        foreach (var (p, reason) in failures)
            Console.WriteLine($"  {Path.GetFileName(p)}: {reason}");
    }
}

// ── Batch XML export / import ─────────────────────────────────────────────────
static void BatchExportXmlUasset(string uassetSrcDir, string xmlOutDir)
{
    var files = Directory.GetFiles(uassetSrcDir, "BMD_*.uasset", SearchOption.AllDirectories);
    Directory.CreateDirectory(xmlOutDir);
    Console.WriteLine($"Exporting {files.Length} BMD uassets → XML (mirroring source tree) ...\n");

    int ok = 0, skip = 0, fail = 0;
    foreach (var uassetPath in files.OrderBy(f => f))
    {
        byte[] data;
        try { data = File.ReadAllBytes(uassetPath); }
        catch (Exception ex) { Console.Error.WriteLine($"  READ FAIL: {Path.GetFileName(uassetPath)}: {ex.Message}"); fail++; continue; }

        int bmdOffset = FindBmdOffset(data);
        if (bmdOffset < 0) { skip++; continue; }

        P3RBmdFile bmd;
        try { bmd = P3RBmdReader.Read(data[bmdOffset..(data.Length - 12)]); }
        catch (Exception ex) { Console.Error.WriteLine($"  PARSE FAIL: {Path.GetFileName(uassetPath)}: {ex.Message}"); fail++; continue; }

        string rel     = Path.GetRelativePath(uassetSrcDir, uassetPath);
        string outPath = Path.Combine(xmlOutDir, Path.ChangeExtension(rel, ".xml"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        try
        {
            string xml = P3RXmlTool.Export(bmd, uassetPath);
            File.WriteAllText(outPath, xml, System.Text.Encoding.UTF8);
            ok++;
        }
        catch (Exception ex) { Console.Error.WriteLine($"  EXPORT FAIL: {Path.GetFileName(uassetPath)}: {ex.Message}"); fail++; }

        if ((ok + fail) % 500 == 0 && (ok + fail) > 0)
            Console.WriteLine($"  ... {ok + fail}/{files.Length}");
    }

    Console.WriteLine($"\nDone: {ok} exported  |  {skip} skipped (no BMD)  |  {fail} errors");
    Console.WriteLine($"XML files at: {Path.GetFullPath(xmlOutDir)}");
}

static void BatchImportXml(string xmlDir, string uassetSrcDir, string modAssetsDir, bool force = false)
{
    var files = Directory.GetFiles(xmlDir, "*.xml", SearchOption.AllDirectories);
    Console.WriteLine($"Importing {files.Length} XML files → changed uassets ({(force ? "force" : "changed-only")}) ...\n");

    int ok = 0, fail = 0, noUasset = 0, unchanged = 0, removedStale = 0;
    foreach (var xmlPath in files)
    {
        string xmlText;
        try { xmlText = File.ReadAllText(xmlPath, System.Text.Encoding.UTF8); }
        catch (Exception ex) { Console.Error.WriteLine($"  READ FAIL: {Path.GetFileName(xmlPath)}: {ex.Message}"); fail++; continue; }

        // xml relative path mirrors uasset tree
        string xmlRel       = Path.GetRelativePath(xmlDir, xmlPath);
        string uassetSrcPath = Path.Combine(uassetSrcDir, Path.ChangeExtension(xmlRel, ".uasset"));
        string outPath = Path.Combine(modAssetsDir, Path.GetRelativePath(uassetSrcDir, uassetSrcPath));
        if (!File.Exists(uassetSrcPath))
        {
            Console.Error.WriteLine($"  NO UASSET: {Path.ChangeExtension(xmlRel, ".uasset")} not found"); noUasset++; continue;
        }

        byte[] uasset;
        try { uasset = File.ReadAllBytes(uassetSrcPath); }
        catch (Exception ex) { Console.Error.WriteLine($"  UASSET READ FAIL: {Path.GetFileName(uassetSrcPath)}: {ex.Message}"); fail++; continue; }

        int bmdOffset = FindBmdOffset(uasset);
        if (bmdOffset < 0) { Console.Error.WriteLine($"  NO BMD IN UASSET: {Path.GetFileName(uassetSrcPath)}"); fail++; continue; }

        P3RBmdFile origBmd;
        try { origBmd = P3RBmdReader.Read(uasset[bmdOffset..]); }
        catch (Exception ex) { Console.Error.WriteLine($"  BMD PARSE FAIL: {Path.GetFileName(uassetSrcPath)}: {ex.Message}"); fail++; continue; }

        if (!force)
        {
            bool hasChanges;
            try { hasChanges = P3RXmlTool.HasChanges(origBmd, xmlText); }
            catch (Exception ex) { Console.Error.WriteLine($"  XML CHECK FAIL: {Path.GetFileName(xmlPath)}: {ex.Message}"); fail++; continue; }

            if (!hasChanges)
            {
                unchanged++;
                if ((ok + fail + noUasset + unchanged) % 200 == 0 && (ok + fail + noUasset + unchanged) > 0)
                    Console.WriteLine($"  ... {ok + fail + noUasset + unchanged}/{files.Length}");
                continue;
            }
        }

        byte[] newBmdData;
        try
        {
            var newBmd = P3RXmlTool.Apply(origBmd, xmlText);
            newBmdData = P3RBmdWriter.Write(newBmd);
        }
        catch (Exception ex) { Console.Error.WriteLine($"  XML APPLY FAIL: {Path.GetFileName(xmlPath)}: {ex.Message}"); fail++; continue; }

        try { RepackUasset(uassetSrcPath, newBmdData, outPath); ok++; }
        catch (Exception ex) { Console.Error.WriteLine($"  REPACK FAIL: {Path.GetFileName(uassetSrcPath)}: {ex.Message}"); fail++; }

        if ((ok + fail + noUasset + unchanged) % 200 == 0 && (ok + fail + noUasset + unchanged) > 0)
            Console.WriteLine($"  ... {ok + fail + noUasset + unchanged}/{files.Length}");
    }

    Console.WriteLine($"\nDone: {ok} changed  |  {unchanged} unchanged  |  {removedStale} removed stale  |  {noUasset} skipped (no uasset)  |  {fail} errors");
}

static void BatchRoundtripXmlUasset(string xmlDir, string uassetSrcDir)
{
    var files = Directory.GetFiles(xmlDir, "*.xml", SearchOption.AllDirectories);
    Console.WriteLine($"XML roundtrip check: {files.Length} files ...\n");

    int perfect = 0, relocOnly = 0, diff = 0, fail = 0;
    var failures = new List<string>();

    foreach (var xmlPath in files)
    {
        string xmlText;
        try { xmlText = File.ReadAllText(xmlPath, System.Text.Encoding.UTF8); }
        catch (Exception ex) { failures.Add($"  READ FAIL: {Path.GetFileName(xmlPath)}: {ex.Message}"); fail++; continue; }

        string xmlRel        = Path.GetRelativePath(xmlDir, xmlPath);
        string uassetSrcPath = Path.Combine(uassetSrcDir, Path.ChangeExtension(xmlRel, ".uasset"));
        if (!File.Exists(uassetSrcPath))
        { failures.Add($"  NO UASSET: {xmlRel}"); fail++; continue; }

        byte[] uasset;
        try { uasset = File.ReadAllBytes(uassetSrcPath); }
        catch (Exception ex) { failures.Add($"  UASSET READ FAIL: {Path.GetFileName(uassetSrcPath)}: {ex.Message}"); fail++; continue; }

        int bmdOffset = FindBmdOffset(uasset);
        if (bmdOffset < 0) { failures.Add($"  NO BMD: {Path.GetFileName(uassetSrcPath)}"); fail++; continue; }

        byte[] origBmdBytes = uasset[bmdOffset..(uasset.Length - 12)];

        P3RBmdFile origBmd;
        try { origBmd = P3RBmdReader.Read(origBmdBytes); }
        catch (Exception ex) { failures.Add($"  PARSE FAIL: {Path.GetFileName(uassetSrcPath)}: {ex.Message}"); fail++; continue; }

        byte[] newBmdBytes;
        try
        {
            var newBmd = P3RXmlTool.Apply(origBmd, xmlText);
            newBmdBytes = P3RBmdWriter.Write(newBmd);
        }
        catch (Exception ex) { failures.Add($"  APPLY FAIL: {Path.GetFileName(xmlPath)}: {ex.Message}"); fail++; continue; }

        int limit = Math.Min(origBmdBytes.Length, newBmdBytes.Length);
        int diffs = 0;
        for (int i = 0; i < limit; i++)
            if (origBmdBytes[i] != newBmdBytes[i]) diffs++;
        if (origBmdBytes.Length != newBmdBytes.Length) diffs += Math.Abs(origBmdBytes.Length - newBmdBytes.Length);

        if (diffs == 0)      perfect++;
        else if (diffs <= 8) relocOnly++;
        else
        {
            diff++;
            if (failures.Count < 20)
                failures.Add($"  DIFF {diffs,6}: {xmlRel} (orig={origBmdBytes.Length} new={newBmdBytes.Length})");
        }
    }

    int total = perfect + relocOnly + diff + fail;
    Console.WriteLine($"Results ({total} files):");
    Console.WriteLine($"  Byte-identical:              {perfect,6}  ({perfect * 100.0 / total:F1}%)");
    Console.WriteLine($"  Reloc-only diff (≤8 bytes):  {relocOnly,6}  ({relocOnly * 100.0 / total:F1}%)");
    Console.WriteLine($"  Content diff (>8 bytes):     {diff,6}");
    Console.WriteLine($"  Errors:                      {fail,6}");

    if (failures.Count > 0)
    {
        Console.WriteLine("\nIssues:");
        foreach (var f in failures) Console.WriteLine(f);
    }
    else
    {
        Console.WriteLine("\nAll clean — XML roundtrip is lossless.");
    }
}

// ── Batch XLIFF export / import ───────────────────────────────────────────────
static void BatchExportXliff(string bmdDir, string xliffOutDir)
{
    var files = Directory.GetFiles(bmdDir, "*.bmd", SearchOption.AllDirectories);
    Directory.CreateDirectory(xliffOutDir);
    Console.WriteLine($"Exporting {files.Length} BMD files → XLIFF ...\n");

    int ok = 0, fail = 0;
    foreach (var path in files)
    {
        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch (Exception ex) { Console.Error.WriteLine($"  READ FAIL: {Path.GetFileName(path)}: {ex.Message}"); fail++; continue; }

        P3RBmdFile bmd;
        try { bmd = P3RBmdReader.Read(data); }
        catch (Exception ex) { Console.Error.WriteLine($"  PARSE FAIL: {Path.GetFileName(path)}: {ex.Message}"); fail++; continue; }

        // Preserve relative sub-directory structure
        string rel     = Path.GetRelativePath(bmdDir, path);
        string outPath = Path.Combine(xliffOutDir, Path.ChangeExtension(rel, ".xliff"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        try
        {
            string xliff = P3RXliffTool.Export(bmd, path);
            File.WriteAllText(outPath, xliff, System.Text.Encoding.UTF8);
            ok++;
        }
        catch (Exception ex) { Console.Error.WriteLine($"  EXPORT FAIL: {Path.GetFileName(path)}: {ex.Message}"); fail++; }

        if ((ok + fail) % 500 == 0)
            Console.WriteLine($"  ... {ok + fail}/{files.Length}");
    }

    Console.WriteLine($"\nDone: {ok} exported  |  {fail} errors");
    Console.WriteLine($"XLIFF files at: {Path.GetFullPath(xliffOutDir)}");
}

static void BatchExportXliffUasset(string uassetSrcDir, string xliffOutDir)
{
    var files = Directory.GetFiles(uassetSrcDir, "BMD_*.uasset", SearchOption.AllDirectories);
    Directory.CreateDirectory(xliffOutDir);
    Console.WriteLine($"Exporting {files.Length} BMD uassets → XLIFF (mirroring source tree) ...\n");

    int ok = 0, skip = 0, fail = 0;
    foreach (var uassetPath in files.OrderBy(f => f))
    {
        byte[] data;
        try { data = File.ReadAllBytes(uassetPath); }
        catch (Exception ex) { Console.Error.WriteLine($"  READ FAIL: {Path.GetFileName(uassetPath)}: {ex.Message}"); fail++; continue; }

        int bmdOffset = FindBmdOffset(data);
        if (bmdOffset < 0) { skip++; continue; }

        P3RBmdFile bmd;
        try { bmd = P3RBmdReader.Read(data[bmdOffset..(data.Length - 12)]); }
        catch (Exception ex) { Console.Error.WriteLine($"  PARSE FAIL: {Path.GetFileName(uassetPath)}: {ex.Message}"); fail++; continue; }

        string rel     = Path.GetRelativePath(uassetSrcDir, uassetPath);
        string outPath = Path.Combine(xliffOutDir, Path.ChangeExtension(rel, ".xliff"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        try
        {
            string xliff = P3RXliffTool.Export(bmd, uassetPath);
            File.WriteAllText(outPath, xliff, System.Text.Encoding.UTF8);
            ok++;
        }
        catch (Exception ex) { Console.Error.WriteLine($"  EXPORT FAIL: {Path.GetFileName(uassetPath)}: {ex.Message}"); fail++; }

        if ((ok + fail) % 500 == 0 && (ok + fail) > 0)
            Console.WriteLine($"  ... {ok + fail}/{files.Length}");
    }

    Console.WriteLine($"\nDone: {ok} exported  |  {skip} skipped (no BMD)  |  {fail} errors");
    Console.WriteLine($"XLIFF files at: {Path.GetFullPath(xliffOutDir)}");
}

static void BatchImportXliff(string xliffDir, string uassetSrcDir, string modAssetsDir)
{
    var files = Directory.GetFiles(xliffDir, "*.xliff", SearchOption.AllDirectories);
    Console.WriteLine($"Importing {files.Length} XLIFF files → translated uassets ...\n");

    // Build filename index: basename (no ext) → ordered list of all matching paths.
    // This handles duplicate names in different subdirs: BMD_Foo.uasset may appear twice,
    // and their extracted BMDs are named BMD_Foo.bmd / BMD_Foo_2.bmd / BMD_Foo_3.bmd.
    Console.WriteLine("Indexing uassets ...");
    var uassetIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var ua in Directory.GetFiles(uassetSrcDir, "*.uasset", SearchOption.AllDirectories))
    {
        string key = Path.GetFileNameWithoutExtension(ua);
        if (!uassetIndex.TryGetValue(key, out var list)) uassetIndex[key] = list = new List<string>();
        list.Add(ua);
    }
    Console.WriteLine($"  {uassetIndex.Values.Sum(l => l.Count)} uassets indexed ({uassetIndex.Count} unique names).\n");

    int ok = 0, fail = 0, noUasset = 0, unchanged = 0, removedStale = 0;
    foreach (var xliffPath in files)
    {
        string xliffText;
        try { xliffText = File.ReadAllText(xliffPath, System.Text.Encoding.UTF8); }
        catch (Exception ex) { Console.Error.WriteLine($"  READ FAIL: {Path.GetFileName(xliffPath)}: {ex.Message}"); fail++; continue; }

        // Resolve matching uasset.
        // Strategy 1 (tree layout): xliff relative path mirrors uasset tree —
        //   e.g. xliff/Xrd777/UI/Facility/BMD_Quest.xliff → source/Xrd777/UI/Facility/BMD_Quest.uasset
        string xliffRel         = Path.GetRelativePath(xliffDir, xliffPath);
        string candidateByPath  = Path.Combine(uassetSrcDir, Path.ChangeExtension(xliffRel, ".uasset"));
        string uassetSrcPath;
        if (File.Exists(candidateByPath))
        {
            uassetSrcPath = candidateByPath;
        }
        else
        {
            // Strategy 2 (flat layout): match by filename, with _N suffix for duplicates.
            string bmdBaseName = Path.GetFileNameWithoutExtension(xliffPath);
            string uassetKey   = bmdBaseName;
            int    uassetSlot  = 0;
            var    suffixMatch = System.Text.RegularExpressions.Regex.Match(bmdBaseName, @"_(\d+)$");
            if (suffixMatch.Success && !uassetIndex.ContainsKey(bmdBaseName))
            {
                uassetSlot = int.Parse(suffixMatch.Groups[1].Value) - 1;
                uassetKey  = bmdBaseName[..^suffixMatch.Value.Length];
            }
            if (!uassetIndex.TryGetValue(uassetKey, out var uassetCandidates) || uassetSlot >= uassetCandidates.Count)
            {
                Console.Error.WriteLine($"  NO UASSET: {uassetKey}.uasset (slot {uassetSlot}) not found in {uassetSrcDir}"); noUasset++; continue;
            }
            uassetSrcPath = uassetCandidates[uassetSlot];
        }

        byte[] uasset;
        try { uasset = File.ReadAllBytes(uassetSrcPath); }
        catch (Exception ex) { Console.Error.WriteLine($"  UASSET READ FAIL: {Path.GetFileName(uassetSrcPath)}: {ex.Message}"); fail++; continue; }

        int bmdOffset = FindBmdOffset(uasset);
        if (bmdOffset < 0) { Console.Error.WriteLine($"  NO BMD IN UASSET: {Path.GetFileName(uassetSrcPath)}"); fail++; continue; }

        // If the XLIFF has no translated targets, the output is bit-identical to the source.
        // Copy the original uasset directly — avoids any BMD re-serialisation differences.
        if (!P3RXliffTool.HasTranslations(xliffText))
        {
            string outPathDirect = Path.Combine(modAssetsDir, Path.GetRelativePath(uassetSrcDir, uassetSrcPath));
            Directory.CreateDirectory(Path.GetDirectoryName(outPathDirect)!);
            try { File.WriteAllBytes(outPathDirect, uasset); ok++; }
            catch (Exception ex) { Console.Error.WriteLine($"  WRITE FAIL: {outPathDirect}: {ex.Message}"); fail++; }
            continue;
        }

        // Extract original BMD from uasset, apply XLIFF translation
        P3RBmdFile origBmd;
        try { origBmd = P3RBmdReader.Read(uasset[bmdOffset..]); }
        catch (Exception ex) { Console.Error.WriteLine($"  BMD PARSE FAIL: {Path.GetFileName(uassetSrcPath)}: {ex.Message}"); fail++; continue; }

        byte[] newBmdData;
        try
        {
            var newBmd = P3RXliffTool.Apply(origBmd, xliffText);
            newBmdData = P3RBmdWriter.Write(newBmd);
        }
        catch (Exception ex) { Console.Error.WriteLine($"  XLIFF APPLY FAIL: {Path.GetFileName(xliffPath)}: {ex.Message}"); fail++; continue; }

        // Output path: modAssetsDir + relative path of uasset within uassetSrcDir
        string relUasset = Path.GetRelativePath(uassetSrcDir, uassetSrcPath);
        string outPath   = Path.Combine(modAssetsDir, relUasset);

        try { RepackUasset(uassetSrcPath, newBmdData, outPath); ok++; }
        catch (Exception ex) { Console.Error.WriteLine($"  REPACK FAIL: {Path.GetFileName(uassetSrcPath)}: {ex.Message}"); fail++; }

        if ((ok + fail + noUasset) % 200 == 0)
            Console.WriteLine($"  ... {ok + fail + noUasset}/{files.Length}");
    }

    Console.WriteLine($"\nDone: {ok} changed  |  {unchanged} unchanged  |  {removedStale} removed stale  |  {noUasset} skipped (no uasset)  |  {fail} errors");
}

// ── UE4 uasset diagnostics ────────────────────────────────────────────────────
static void DiagUasset(string path)
{
    byte[]? data = TryRead(path);
    if (data == null) { PauseOnError(); return; }

    int bmdOffset = FindBmdOffset(data);
    if (bmdOffset < 0) { Console.Error.WriteLine("No BMD found."); PauseOnError(); return; }

    var (storedBmdSize, overhead) = ReadUassetSizeFields(data, bmdOffset);
    long serialSize   = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
        new ReadOnlySpan<byte>(data, bmdOffset - 0x81, 8));
    long serialOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
        new ReadOnlySpan<byte>(data, bmdOffset - 0x79, 8));   // int64 right after SerialSize

    int actualBmdSize = data.Length - 12 - bmdOffset;

    Console.WriteLine($"File:          {Path.GetFileName(path)}  ({data.Length} bytes)");
    Console.WriteLine($"bmdOffset:     0x{bmdOffset:X} ({bmdOffset})");
    Console.WriteLine($"storedBmdSize: {storedBmdSize}  (at bmdOffset-4)");
    Console.WriteLine($"actualBmdSize: {actualBmdSize}  (fileSize - 12 - bmdOffset)");
    Console.WriteLine($"serialSize:    {serialSize}  (at bmdOffset-0x81)");
    Console.WriteLine($"serialOffset:  {serialOffset}  (at bmdOffset-0x79)");
    Console.WriteLine($"overhead:      {overhead}  (serialSize - storedBmdSize)");
    Console.WriteLine($"serialEnd:     {serialOffset + serialSize}  (serialOffset + serialSize)");
    Console.WriteLine($"fileEnd:       {data.Length}");
    Console.WriteLine($"tailCheck:     {(serialOffset + serialSize == data.Length ? "OK (serialEnd == fileSize)" : $"MISMATCH (expected {data.Length})")}");

    Console.WriteLine($"\nBytes at [bmdOffset-0x81, bmdOffset)  (pre-BMD UE4 data, {0x81} bytes):");
    var preBmd = data[(bmdOffset - 0x81)..bmdOffset];
    for (int i = 0; i < preBmd.Length; i += 16)
    {
        int len = Math.Min(16, preBmd.Length - i);
        Console.Write($"  {bmdOffset - 0x81 + i:X4}: ");
        for (int j = 0; j < len; j++) Console.Write($"{preBmd[i+j]:X2} ");
        Console.WriteLine();
    }

    Console.WriteLine($"\nFirst 16 bytes of BMD (at bmdOffset=0x{bmdOffset:X}):");
    Console.Write("  ");
    for (int i = 0; i < Math.Min(16, actualBmdSize); i++) Console.Write($"{data[bmdOffset+i]:X2} ");
    Console.WriteLine();

    Console.WriteLine($"\nTail (last 12 bytes):");
    Console.Write("  ");
    for (int i = data.Length - 12; i < data.Length; i++) Console.Write($"{data[i]:X2} ");
    Console.WriteLine();
}

// ── Reloc table checker ───────────────────────────────────────────────────────
static bool CheckReloc(string uassetPath, bool quiet = false)
{
    byte[]? data = TryRead(uassetPath);
    if (data == null) { if (!quiet) PauseOnError(); return false; }

    int bmdOffset = FindBmdOffset(data);
    if (bmdOffset < 0) { Console.Error.WriteLine($"ERROR: no BMD found in {Path.GetFileName(uassetPath)}."); if (!quiet) PauseOnError(); return false; }

    byte[] bmdSrc = data[bmdOffset..(data.Length - 12)];

    // Decode source reloc table
    int srcRelOff  = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(bmdSrc,  16, 4));
    int srcRelSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(bmdSrc,  20, 4));
    byte[] srcRelTable = bmdSrc[srcRelOff..(srcRelOff + srcRelSize)];
    var srcPtrs = RelocationTableEncoding.Decode(srcRelTable, 32);

    if (!quiet)
    {
        Console.WriteLine($"Source BMD:   {bmdSrc.Length} bytes  reloc_off={srcRelOff}  reloc_size={srcRelSize}  pointers={srcPtrs.Count}");
        Console.WriteLine($"  First 5 ptrs: {string.Join(", ", srcPtrs.Take(5))}");
        Console.WriteLine($"  Last  5 ptrs: {string.Join(", ", srcPtrs.TakeLast(5))}");
    }

    // Re-serialise the BMD and decode its reloc table
    P3RBmdFile bmd;
    try { bmd = P3RBmdReader.Read(bmdSrc); }
    catch (Exception ex) { Console.Error.WriteLine($"READ ERROR: {ex.Message}"); if (!quiet) PauseOnError(); return false; }

    byte[] bmdOut = P3RBmdWriter.Write(bmd);
    int outRelOff  = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(bmdOut,  16, 4));
    int outRelSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(bmdOut,  20, 4));
    byte[] outRelTable = bmdOut[outRelOff..(outRelOff + outRelSize)];
    var outPtrs = RelocationTableEncoding.Decode(outRelTable, 32);

    if (!quiet)
    {
        int totalPagesOut = 0;
        Console.WriteLine($"\nDialog breakdown (dialogs={bmd.Dialogs.Length} speakers={bmd.Speakers.Names.Length}):");
        for (int i = 0; i < bmd.Dialogs.Length; i++)
        {
            int count = bmd.Dialogs[i] switch {
                P3RMessageDialog m  => m.Pages.Length,
                P3RSelectionDialog s => s.Options.Length,
                _ => 0
            };
            totalPagesOut += count;
        }
        Console.WriteLine($"  Total pages/options from reader: {totalPagesOut}");
        Console.WriteLine($"  Expected (from source reloc pointers - dialogs - speakerTable - speakers): {srcPtrs.Count - bmd.Dialogs.Length - 1 - bmd.Speakers.Names.Length}");
        Console.WriteLine($"\n  Dialog headers: {bmd.Dialogs.Length}  Speaker table: 1  Speaker names: {bmd.Speakers.Names.Length}");
        Console.WriteLine($"  Source page pointers:  {srcPtrs.Count  - bmd.Dialogs.Length - 1 - bmd.Speakers.Names.Length}");
        int outPagePtrs = outPtrs.Count - bmd.Dialogs.Length - 1 - bmd.Speakers.Names.Length;
        Console.WriteLine($"  Output  page pointers: {outPagePtrs}  (delta {outPagePtrs - (srcPtrs.Count - bmd.Dialogs.Length - 1 - bmd.Speakers.Names.Length)})");

        Console.WriteLine("\n  Dialogs with non-zero page/option count:");
        int sumPages = 0;
        for (int i = 0; i < bmd.Dialogs.Length; i++)
        {
            int cnt = bmd.Dialogs[i] switch {
                P3RMessageDialog m   => m.Pages.Length,
                P3RSelectionDialog s => s.Options.Length,
                _ => 0 };
            sumPages += cnt;
            if (cnt > 0)
                Console.WriteLine($"    [{i:D3}] {(bmd.Dialogs[i] is P3RSelectionDialog ? "Sel" : "Msg")} '{bmd.Dialogs[i].Name}' pages={cnt}");
        }
        Console.WriteLine($"  Sum: {sumPages} pages from reader");

        Console.WriteLine($"\nRebuilt BMD:  {bmdOut.Length} bytes  reloc_off={outRelOff}  reloc_size={outRelSize}  pointers={outPtrs.Count}");
        Console.WriteLine($"  First 5 ptrs: {string.Join(", ", outPtrs.Take(5))}");
        Console.WriteLine($"  Last  5 ptrs: {string.Join(", ", outPtrs.TakeLast(5))}");
    }

    // Compare
    bool pass;
    if (srcPtrs.Count != outPtrs.Count)
    {
        pass = false;
        if (quiet)
        {
            Console.WriteLine($"RELOC MISMATCH: {Path.GetFileName(uassetPath)}  src={srcPtrs.Count} out={outPtrs.Count}");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"POINTER COUNT MISMATCH: src={srcPtrs.Count} out={outPtrs.Count}  delta={outPtrs.Count - srcPtrs.Count}");
            int minLen = Math.Min(srcPtrs.Count, outPtrs.Count);
            bool shownDiff = false;
            for (int i = 0; i < minLen; i++)
            {
                if (srcPtrs[i] != outPtrs[i])
                {
                    Console.WriteLine($"  First value diff at index {i}: src={srcPtrs[i]} out={outPtrs[i]}");
                    shownDiff = true;
                    break;
                }
            }
            if (!shownDiff) Console.WriteLine($"  Values identical up to index {minLen-1}, then one list is longer.");
        }
    }
    else
    {
        int diffs = srcPtrs.Zip(outPtrs).Count(p => p.First != p.Second);
        pass = diffs == 0;
        if (quiet)
        {
            if (!pass) Console.WriteLine($"RELOC MISMATCH: {Path.GetFileName(uassetPath)}  {diffs} value diffs (count={srcPtrs.Count})");
        }
        else
        {
            Console.WriteLine();
            if (pass)
                Console.WriteLine($"RELOC OK — {srcPtrs.Count} pointers, all values identical.");
            else
            {
                Console.WriteLine($"POINTER VALUES DIFFER: {diffs} mismatches (count same = {srcPtrs.Count})");
                int shown = 0;
                foreach (var (s, o, idx) in srcPtrs.Zip(outPtrs).Select((p,i)=>(p.First,p.Second,i)))
                {
                    if (s != o && shown++ < 10)
                        Console.WriteLine($"  [{idx}] src={s} out={o}  delta={o-s}");
                }
            }
        }
    }
    return pass;
}

// ── Mod folder setup ──────────────────────────────────────────────────────────
static void MakeMod(string uassetSrcDir, string modDir)
{
    string assetsOut = Path.Combine(modDir, "UnrealEssentials", "P3R", "Content", "L10N", "en");
    Directory.CreateDirectory(assetsOut);

    // Write ModConfig.json
    string modId   = Path.GetFileName(modDir);
    string cfgPath = Path.Combine(modDir, "ModConfig.json");
    string cfg = $$"""
{
  "ModId": "{{modId}}",
  "ModName": "Ukrainian Localization",
  "ModAuthor": "",
  "ModVersion": "0.1",
  "ModDescription": "Ukrainian language localization for Persona 3 Reload",
  "ModDll": "",
  "ModIcon": "",
  "ModR2RManagedDll32": "",
  "ModR2RManagedDll64": "",
  "ModNativeDll32": "",
  "ModNativeDll64": "",
  "Tags": [],
  "CanUnload": null,
  "HasExports": null,
  "IsLibrary": false,
  "ReleaseMetadataFileName": "{{modId}}.ReleaseMetadata.json",
  "PluginData": {
    "GameBananaDependencies": { "IdToConfigMap": {} },
    "GitHubDependencies": {
      "IdToConfigMap": {
        "p3rpc.essentials": {
          "Config": { "UserName": "AnimatedSwine37", "RepositoryName": "p3rpc.essentials", "UseReleaseTag": true, "AssetFileName": "Mod.zip" },
          "ReleaseMetadataName": "p3rpc.essentials.ReleaseMetadata.json"
        },
        "UnrealEssentials": {
          "Config": { "UserName": "AnimatedSwine37", "RepositoryName": "UnrealEssentials", "UseReleaseTag": false, "AssetFileName": "Mod.zip" },
          "ReleaseMetadataName": "UnrealEssentials.ReleaseMetadata.json"
        },
        "UTOC.Stream.Emulator": {
          "Config": { "UserName": "AnimatedSwine37", "RepositoryName": "UnrealEssentials", "UseReleaseTag": false, "AssetFileName": "Mod.zip" },
          "ReleaseMetadataName": "UTOC.Stream.Emulator.ReleaseMetadata.json"
        }
      }
    }
  },
  "IsUniversalMod": false,
  "ModDependencies": [
    "p3rpc.essentials",
    "UnrealEssentials",
    "UTOC.Stream.Emulator"
  ],
  "OptionalDependencies": [],
  "SupportedAppId": ["p3r.exe"],
  "ProjectUrl": ""
}
""";
    File.WriteAllText(cfgPath, cfg, System.Text.Encoding.UTF8);
    Console.WriteLine($"  Created: {cfgPath}");

    // Copy all uassets, preserving directory structure
    var uassets = Directory.GetFiles(uassetSrcDir, "*.uasset", SearchOption.AllDirectories);
    Console.WriteLine($"\nCopying {uassets.Length} uassets from original English export ...");

    int copied = 0, fail = 0;
    foreach (var src in uassets)
    {
        string rel  = Path.GetRelativePath(uassetSrcDir, src);
        string dest = Path.Combine(assetsOut, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        try { File.Copy(src, dest, overwrite: true); copied++; }
        catch (Exception ex) { Console.Error.WriteLine($"  COPY FAIL: {rel}: {ex.Message}"); fail++; }

        if (copied % 500 == 0 && copied > 0)
            Console.WriteLine($"  ... {copied}/{uassets.Length}");
    }

    Console.WriteLine($"\nDone: {copied} copied  |  {fail} errors");
    Console.WriteLine($"Mod folder: {Path.GetFullPath(modDir)}");
    Console.WriteLine($"\nNext steps:");
    Console.WriteLine($"  1. Run --batch-export-xliff <bmd-dir> <xliff-out-dir>  to generate XLIFF files for translators");
    Console.WriteLine($"  2. After translation: --batch-import-xliff <xliff-dir> \"{uassetSrcDir}\" \"{assetsOut}\"");
}

// ── BMD ↔ XLIFF ───────────────────────────────────────────────────────────────
static void ExportXliff(string bmdPath, string? outPath)
{
    byte[]? data = TryRead(bmdPath);
    if (data == null) { PauseOnError(); return; }

    P3RBmdFile bmd;
    try { bmd = P3RBmdReader.Read(data); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR reading BMD: {ex.Message}"); PauseOnError(); return; }

    string xliff = P3RXliffTool.Export(bmd, bmdPath);
    outPath ??= Path.ChangeExtension(bmdPath, ".xliff");
    File.WriteAllText(outPath, xliff, System.Text.Encoding.UTF8);
    Console.WriteLine($"{Path.GetFileName(bmdPath)} → {Path.GetFileName(outPath)}  ({bmd.Dialogs.Length} dialogs)");
}

static void ImportXliff(string xliffPath, string? outPath)
{
    string xliffText;
    try { xliffText = File.ReadAllText(xliffPath, System.Text.Encoding.UTF8); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR reading XLIFF: {ex.Message}"); PauseOnError(); return; }

    string bmdPath;
    string sourcePath = P3RXliffTool.GetSourcePath(xliffText);
    if (sourcePath.Length > 0 && File.Exists(sourcePath))
        bmdPath = sourcePath;
    else
    {
        bmdPath = Path.ChangeExtension(xliffPath, ".bmd");
        if (!File.Exists(bmdPath))
        {
            Console.Error.WriteLine($"ERROR: cannot find original .bmd for {Path.GetFileName(xliffPath)}");
            Console.Error.WriteLine($"  Looked for: {bmdPath}");
            PauseOnError();
            return;
        }
    }

    byte[]? origData = TryRead(bmdPath);
    if (origData == null) { PauseOnError(); return; }

    P3RBmdFile origBmd;
    try { origBmd = P3RBmdReader.Read(origData); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR reading original BMD: {ex.Message}"); PauseOnError(); return; }

    P3RBmdFile newBmd;
    try { newBmd = P3RXliffTool.Apply(origBmd, xliffText); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR applying XLIFF: {ex.Message}"); PauseOnError(); return; }

    byte[] newData;
    try { newData = P3RBmdWriter.Write(newBmd); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR writing BMD: {ex.Message}"); PauseOnError(); return; }

    outPath ??= Path.ChangeExtension(xliffPath, ".bmd");
    File.WriteAllBytes(outPath, newData);
    Console.WriteLine($"{Path.GetFileName(xliffPath)} → {Path.GetFileName(outPath)}  ({newData.Length} bytes, delta={newData.Length - origData.Length:+#;-#;0})");
}

static void BatchXliff(string dir)
{
    var files = Directory.GetFiles(dir, "*.bmd", SearchOption.AllDirectories);
    Console.WriteLine($"Testing {files.Length} BMD files (XLIFF round-trip) ...\n");

    int perfect = 0, relocOnly = 0, semanticDiff = 0, parseFail = 0, total = 0;
    var failures = new List<string>();

    foreach (var path in files)
    {
        total++;
        byte[] orig;
        try { orig = File.ReadAllBytes(path); }
        catch { parseFail++; continue; }

        byte[] rebuilt;
        try
        {
            var bmd     = P3RBmdReader.Read(orig);
            string xliff = P3RXliffTool.Export(bmd, path);
            var bmd2    = P3RXliffTool.Apply(bmd, xliff);
            rebuilt     = P3RBmdWriter.Write(bmd2);
        }
        catch (Exception ex)
        {
            parseFail++;
            failures.Add($"  FAIL: {Path.GetFileName(path)}: {ex.Message}");
            continue;
        }

        int limit = Math.Min(orig.Length, rebuilt.Length);
        int diffs = 0;
        for (int i = 0; i < limit; i++)
            if (orig[i] != rebuilt[i]) diffs++;

        if (diffs == 0)      perfect++;
        else if (diffs <= 8) relocOnly++;
        else
        {
            semanticDiff++;
            if (failures.Count < 20)
                failures.Add($"  DIFF {diffs,6}: {Path.GetFileName(path)} (orig={orig.Length} rebuilt={rebuilt.Length})");
        }

        if (total % 5000 == 0)
            Console.WriteLine($"  ... {total}/{files.Length}");
    }

    Console.WriteLine($"\nResults (XLIFF round-trip, {total} files):");
    Console.WriteLine($"  Content-perfect (≤8 byte reloc diff): {perfect + relocOnly,6}  ({(perfect + relocOnly) * 100.0 / total:F1}%)");
    Console.WriteLine($"    of which byte-identical:            {perfect,6}");
    Console.WriteLine($"    reloc-table diff only (≤8 bytes):   {relocOnly,6}");
    Console.WriteLine($"  Semantic diff (>8 bytes):             {semanticDiff,6}");
    Console.WriteLine($"  Parse / write failure:                {parseFail,6}");
    if (failures.Count > 0)
    {
        Console.WriteLine("\nFirst failures:");
        foreach (var f in failures) Console.WriteLine(f);
    }
}

// ── Manual uasset repack ──────────────────────────────────────────────────────
// These P3R BMD uassets have a non-standard magic (not C1 83 2A 9E) so UAssetAPI
// cannot parse them. We patch the three size fields manually instead.
//
// Three int fields depend on BMD data size (confirmed by diffing EN vs RU assets):
//   bmdOffset - 0x81  int64 LE  UE4 export SerialSize         = bmdSize + overhead(49)
//   bmdOffset - 21    int32 LE  FProperty tag size             = bmdSize + 4
//   bmdOffset - 4     int32 LE  TArray<uint8> element count   = bmdSize
static void RepackUasset(string sourcePath, byte[] newBmdData, string outputPath)
{
    byte[] orig = File.ReadAllBytes(sourcePath);
    int bmdOffset = FindBmdOffset(orig);
    if (bmdOffset < 0)
        throw new InvalidDataException("1GSM marker not found");

    var (origBmdSize, overhead) = ReadUassetSizeFields(orig, bmdOffset);

    byte[] uassetTail = orig[(orig.Length - 12)..];
    byte[] packed = new byte[bmdOffset + newBmdData.Length + 12];
    orig[..bmdOffset].CopyTo(packed, 0);
    newBmdData.CopyTo(packed, bmdOffset);
    uassetTail.CopyTo(packed, bmdOffset + newBmdData.Length);

    int n = newBmdData.Length;
    // SerialSize (int64 LE)
    System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
        new Span<byte>(packed, bmdOffset - 0x81, 8), n + overhead);
    // FProperty tag size (int32 LE) = bmdSize + 4 (the TArray count prefix)
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
        new Span<byte>(packed, bmdOffset - 21, 4), n + 4);
    // TArray element count (int32 LE)
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
        new Span<byte>(packed, bmdOffset - 4, 4), n);

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllBytes(outputPath, packed);
}

// ── UE4 header helpers ────────────────────────────────────────────────────────

// Returns (origBmdDataSize, serialSizeOverhead) read from the source uasset.
// The int32 LE at [bmdOffset-4] is the stored BMD data size.
// The int64 LE at [bmdOffset-0x81] is the UE4 export SerialSize.
// overhead = SerialSize - bmdDataSize  (constant per asset, typically 49 for P3R).
static (int bmdDataSize, long overhead) ReadUassetSizeFields(byte[] uasset, int bmdOffset)
{
    int  bmdDataSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
        new ReadOnlySpan<byte>(uasset, bmdOffset - 4, 4));
    long serialSize  = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
        new ReadOnlySpan<byte>(uasset, bmdOffset - 0x81, 8));
    return (bmdDataSize, serialSize - bmdDataSize);
}


// ── Helpers ───────────────────────────────────────────────────────────────────
static int FindBmdOffset(byte[] data)
{
    for (int i = 8; i < data.Length - 3; i++)
    {
        if (data[i] == 0x31 && data[i + 1] == 0x47 && data[i + 2] == 0x53 && data[i + 3] == 0x4D)
            return i - 8;
    }
    return -1;
}

static byte[]? TryRead(string path)
{
    try { return File.ReadAllBytes(path); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR reading {path}: {ex.Message}"); return null; }
}

// ── Batch accuracy test ───────────────────────────────────────────────────────
// Reads every .bmd in a directory, rebuilds binary (optionally via XML), compares.
// Reports: byte-perfect | reloc-only | semantic-match | failed-to-parse
static void BatchTest(string dir, bool xml)
{
    var files = Directory.GetFiles(dir, "*.bmd", SearchOption.AllDirectories);
    Console.WriteLine($"Testing {files.Length} BMD files ({(xml ? "XML round-trip" : "binary round-trip")}) ...\n");

    int perfect = 0, relocOnly = 0, semanticMatch = 0, parseFail = 0, total = 0;
    var failures = new List<string>();

    foreach (var path in files)
    {
        total++;
        byte[] orig;
        try { orig = File.ReadAllBytes(path); }
        catch { parseFail++; continue; }

        byte[] rebuilt;
        try
        {
            var bmd = P3RBmdReader.Read(orig);
            if (xml)
            {
                string xmlText = P3RXmlTool.Export(bmd, path);
                bmd = P3RXmlTool.Apply(bmd, xmlText);
            }
            rebuilt = P3RBmdWriter.Write(bmd);
        }
        catch (Exception ex)
        {
            parseFail++;
            failures.Add($"  PARSE FAIL: {Path.GetFileName(path)}: {ex.Message}");
            continue;
        }

        // Compare up to the smaller length (orig may have uasset tail padding)
        int limit = Math.Min(orig.Length, rebuilt.Length);
        int diffs = 0;
        for (int i = 0; i < limit; i++)
            if (orig[i] != rebuilt[i]) diffs++;

        bool sameSize = orig.Length == rebuilt.Length;

        if (diffs == 0 && sameSize)         perfect++;
        else if (diffs == 0)                perfect++;    // tail padding only — content perfect
        else if (diffs <= 8)                relocOnly++;  // reloc table encoding difference only
        else
        {
            semanticMatch++;
            if (failures.Count < 10)
                failures.Add($"  DIFF {diffs,6}: {Path.GetFileName(path)} (orig={orig.Length} rebuilt={rebuilt.Length})");
        }

        if (total % 5000 == 0)
            Console.WriteLine($"  ... {total}/{files.Length}");
    }

    Console.WriteLine($"\nResults ({(xml ? "XML" : "binary")} round-trip, {total} files):");
    Console.WriteLine($"  Content-perfect (≤8 byte reloc diff): {perfect + relocOnly,6}  ({(perfect + relocOnly) * 100.0 / total:F1}%)");
    Console.WriteLine($"    of which byte-identical:            {perfect,6}");
    Console.WriteLine($"    reloc-table diff only (≤8 bytes):   {relocOnly,6}");
    Console.WriteLine($"  Semantic match, larger diff:          {semanticMatch,6}");
    Console.WriteLine($"  Parse / write failure:                {parseFail,6}");
    if (failures.Count > 0)
    {
        Console.WriteLine("\nFirst failures/large-diffs:");
        foreach (var f in failures) Console.WriteLine(f);
    }
}

static void PauseOnError()
{
    if (!Console.IsInputRedirected)
    {
        Console.Write("Press any key to close...");
        Console.ReadKey(intercept: true);
    }
}




