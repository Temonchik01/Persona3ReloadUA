using P3RDtTool;

if (args.Length == 0)
{
    Console.WriteLine("P3RDtTool");
    Console.WriteLine("  --diag <file.uasset>   Dump structure of a P3R DataTable uasset");
    Console.WriteLine("  --scan-text <file.uasset>   Scan cooked DT export data for P3R text blocks");
    Console.WriteLine("  --export-text-xml <file.uasset> <out.xml>   Export detected text blocks");
    Console.WriteLine("  --import-text-xml <file.uasset> <in.xml> <out.uasset>   Import detected text blocks");
    Console.WriteLine("  --scan-fstrings <file.uasset>   Scan export data for FString values");
    Console.WriteLine("  --export-fstrings-xml <file.uasset> <out.xml>   Export FString values");
    Console.WriteLine("  --import-fstrings-xml <file.uasset> <in.xml> <out.uasset>   Import FString values");
    Console.WriteLine("  --batch-export-xml <source-dir> <dt-xml-dir>   Export supported DT XML files");
    Console.WriteLine("  --batch-import-xml <dt-xml-dir> <source-dir> <out-dir> [--force] [--verbose]   Import supported DT XML files");
    return;
}

if (args[0] == "--diag" && args.Length >= 2)
{
    P3RDtUassetDiag.Dump(args[1]);
    return;
}

if (args[0] == "--scan-text" && args.Length >= 2)
{
    P3RDtTextScanner.Scan(args[1]);
    return;
}

if (args[0] == "--export-text-xml" && args.Length >= 3)
{
    P3RDtTextScanner.ExportXml(args[1], args[2]);
    return;
}

if (args[0] == "--import-text-xml" && args.Length >= 4)
{
    P3RDtTextScanner.ImportXml(args[1], args[2], args[3]);
    return;
}

if (args[0] == "--scan-fstrings" && args.Length >= 2)
{
    P3RDtFStringScanner.Scan(args[1]);
    return;
}

if (args[0] == "--export-fstrings-xml" && args.Length >= 3)
{
    P3RDtFStringScanner.ExportXml(args[1], args[2]);
    return;
}

if (args[0] == "--import-fstrings-xml" && args.Length >= 4)
{
    P3RDtFStringScanner.ImportXml(args[1], args[2], args[3]);
    return;
}

if (args[0] == "--batch-export-xml" && args.Length >= 3)
{
    P3RDtBatch.ExportXml(args[1], args[2]);
    return;
}

if (args[0] == "--batch-import-xml" && args.Length >= 4)
{
    P3RDtBatch.ImportXml(args[1], args[2], args[3], force: args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase)), verbose: args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase)));
    return;
}
if (args[0] == "--hexdump" && args.Length >= 4)
{
    var data = File.ReadAllBytes(args[1]);
    int off = Convert.ToInt32(args[2], 16);
    int len = int.Parse(args[3]);
    len = Math.Min(len, data.Length - off);
    for (int i = 0; i < len; i += 16)
    {
        int row = Math.Min(16, len - i);
        var hex = string.Join(" ", data[(off+i)..(off+i+row)].Select(b => b.ToString("X2")));
        var asc = new string(data[(off+i)..(off+i+row)].Select(b => b >= 32 && b < 127 ? (char)b : '.').ToArray());
        Console.WriteLine($"0x{off+i:X4}  {hex,-47}  {asc}");
    }
    return;
}

Console.WriteLine($"Unknown command: {args[0]}");


