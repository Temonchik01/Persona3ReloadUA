using System.Text;
using System.Xml.Linq;

namespace P3RDtTool;

public static class P3RDtFStringScanner
{
    public static void Scan(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var parsed = Parse(data);

        Console.WriteLine($"=== {Path.GetFileName(path)} ({data.Length} bytes) ===");
        Console.WriteLine($"ExportData: 0x{parsed.ExportStart:X}");
        Console.WriteLine();
        Console.WriteLine("Index  Offset    Len  Enc     Text");
        Console.WriteLine(new string('-', 90));

        foreach (var item in parsed.Strings)
            Console.WriteLine($"{item.Index,5}  0x{item.Offset:X6}  {item.Length,4}  {(item.IsWide ? "UTF-16" : "UTF-8 "),-7} {item.Text}");

        Console.WriteLine();
        Console.WriteLine($"Done. Found {parsed.Strings.Count} FString values.");
    }

    public static void ExportXml(string inputPath, string outputPath)
    {
        if (P3RDtArrayStrings.Export(inputPath, outputPath)) return;
        byte[] data = File.ReadAllBytes(inputPath);
        var parsed = Parse(data);
        var textBlocks = DetectTextPropertyBlocks(parsed.Strings);
        var roles = BuildRoles(textBlocks);
        var editable = parsed.Strings
            .Where(item => roles.TryGetValue(item.Index, out var role) && role == "value")
            .Where(item => !IsTechnicalValue(item.Text))
            .ToList();
        string layout = "text-property";
        if (editable.Count == 0)
        {
            editable = parsed.Strings
                .Where(item => IsLooseEditableText(item.Text))
                .ToList();
            layout = "loose-fstrings";
        }

        var doc = new XDocument(
            new XElement("P3RDataTableFStrings",
                new XAttribute("source", Path.GetFileName(inputPath)),
                new XAttribute("layout", layout),
                new XAttribute("stringCount", editable.Count),
                editable.Select(item =>
                    new XElement("String",
                        new XAttribute("index", item.Index),
                        new XAttribute("offset", $"0x{item.Offset:X}"),
                        new XAttribute("length", item.Length),
                        new XAttribute("encoding", item.IsWide ? "utf-16" : "utf-8"),
                        new XAttribute("role", "value"),
                        new XElement("Source", item.Text),
                        new XElement("Translation", item.Text)))));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        doc.Save(outputPath);
        Console.WriteLine($"Exported {editable.Count} editable FString values -> {outputPath}");
    }

    public static void ImportXml(string inputPath, string xmlPath, string outputPath)
    {
        if (P3RDtArrayStrings.Import(inputPath, xmlPath, outputPath, out _)) return;
        byte[] data = File.ReadAllBytes(inputPath);
        var parsed = Parse(data);
        var doc = XDocument.Load(xmlPath);
        bool looseLayout = string.Equals((string?)doc.Root?.Attribute("layout"), "loose-fstrings", StringComparison.Ordinal);
        var translations = doc.Root?
            .Elements("String")
            .Select(e => new
            {
                Index = (int?)e.Attribute("index") ?? -1,
                Offset = ParseHexOffset((string?)e.Attribute("offset")),
                Source = (string?)e.Element("Source") ?? "",
                Translation = (string?)e.Element("Translation") ?? ""
            })
            .ToDictionary(x => x.Index);

        if (translations is null)
            throw new InvalidDataException("Missing XML root.");

        var replacements = parsed.Strings.ToDictionary(
            item => item.Index,
            item =>
            {
                if (!translations.TryGetValue(item.Index, out var entry))
                    return EncodedFString.From(item, item.Text);
                if (entry.Offset != item.Offset)
                    throw new InvalidDataException($"Offset mismatch at index {item.Index}: XML 0x{entry.Offset:X}, file 0x{item.Offset:X}.");
                if (entry.Source != item.Text)
                    throw new InvalidDataException(
                        $"Source mismatch at index {item.Index} / 0x{item.Offset:X}. " +
                        "Do not edit <Source>; put translated text in <Translation>.");

                string text = string.IsNullOrEmpty(entry.Translation) ? entry.Source : entry.Translation;
                return EncodedFString.From(item, text);
            });

        byte[] patchedData = data.ToArray();
        var textBlocks = DetectTextPropertyBlocks(parsed.Strings);
        var roles = BuildRoles(textBlocks);
        var coveredStrings = new HashSet<int>();
        foreach (var block in textBlocks)
        {
            int delta = 0;
            foreach (int index in block.StringIndexes)
            {
                coveredStrings.Add(index);
                delta += replacements[index].ByteCount - parsed.Strings[index].ByteCount;
            }

            if (delta == 0)
                continue;

            long oldSize = RI64(patchedData, block.SizeOffset);
            if (oldSize <= 0 || oldSize > patchedData.Length)
                throw new InvalidDataException($"Bad TextProperty size at 0x{block.SizeOffset:X}.");

            WriteI64(patchedData, block.SizeOffset, oldSize + delta);
        }

        foreach (var item in parsed.Strings)
        {
            if (replacements[item.Index].Text != item.Text &&
                !looseLayout &&
                (!roles.TryGetValue(item.Index, out string? role) || role != "value"))
            {
                throw new InvalidDataException(
                    $"Unsafe FString edit at index {item.Index} / 0x{item.Offset:X}: " +
                    $"role '{role ?? "unknown"}' is not translatable.");
            }
            if (replacements[item.Index].Text != item.Text && IsTechnicalValue(item.Text))
            {
                throw new InvalidDataException(
                    $"Unsafe FString edit at index {item.Index} / 0x{item.Offset:X}: " +
                    $"technical value '{item.Text}' is not translatable.");
            }

            int delta = replacements[item.Index].ByteCount - item.ByteCount;
            if (delta != 0 && !coveredStrings.Contains(item.Index) && !looseLayout)
                throw new InvalidDataException(
                    $"Unsafe FString resize at index {item.Index} / 0x{item.Offset:X}: " +
                    "the string is not inside a recognized DT TextProperty block.");
        }

        using var ms = new MemoryStream(data.Length + replacements.Values.Sum(x => x.ByteCount) - parsed.Strings.Sum(x => x.ByteCount));
        int cursor = 0;
        int changed = 0;

        foreach (var item in parsed.Strings)
        {
            var replacement = replacements[item.Index];
            ms.Write(patchedData, cursor, item.Offset - cursor);
            ms.Write(BitConverter.GetBytes(replacement.Length));
            ms.Write(replacement.Bytes);
            if (replacement.IsWide)
            {
                ms.WriteByte(0);
                ms.WriteByte(0);
            }
            else
            {
                ms.WriteByte(0);
            }

            cursor = item.Offset + 4 + item.ByteCount;
            if (replacement.Text != item.Text || replacement.IsWide != item.IsWide)
                changed++;
        }

        ms.Write(patchedData, cursor, data.Length - cursor);
        byte[] output = ms.ToArray();
        long newExportSize = output.Length - parsed.ExportStart;
        WriteI64(output, parsed.ExportOffset + 8, newExportSize);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, output);
        Console.WriteLine($"Imported {parsed.Strings.Count} FString values ({changed} changed) -> {outputPath}");
    }

    public static int CountChanges(string inputPath, string xmlPath)
    {
        if (P3RDtArrayStrings.Import(inputPath, xmlPath, null, out int arrayChanges)) return arrayChanges;
        byte[] data = File.ReadAllBytes(inputPath);
        var parsed = Parse(data);
        var doc = XDocument.Load(xmlPath);
        var translations = doc.Root?
            .Elements("String")
            .Select(e => new
            {
                Index = (int?)e.Attribute("index") ?? -1,
                Offset = ParseHexOffset((string?)e.Attribute("offset")),
                Source = (string?)e.Element("Source") ?? "",
                Translation = (string?)e.Element("Translation") ?? ""
            })
            .ToDictionary(x => x.Index);

        if (translations is null)
            throw new InvalidDataException("Missing XML root.");

        var byIndex = parsed.Strings.ToDictionary(item => item.Index);
        int changed = 0;

        foreach (var entry in translations.Values)
        {
            if (!byIndex.TryGetValue(entry.Index, out var item))
                throw new InvalidDataException($"XML String index {entry.Index} was not found in source file.");
            if (entry.Offset != item.Offset)
                throw new InvalidDataException($"Offset mismatch at index {item.Index}: XML 0x{entry.Offset:X}, file 0x{item.Offset:X}.");
            if (entry.Source != item.Text)
                throw new InvalidDataException(
                    $"Source mismatch at index {item.Index} / 0x{item.Offset:X}. " +
                    "Do not edit <Source>; put translated text in <Translation>.");

            string replacement = string.IsNullOrEmpty(entry.Translation) ? entry.Source : entry.Translation;
            if (replacement != item.Text)
                changed++;
        }

        return changed;
    }
    private static ParsedFile Parse(byte[] data)
    {
        int exportOffset = RI32(data, 0x2C);
        int exportStart = RI32(data, 0x34) + RI32(data, 0x38);
        var strings = new List<FStringEntry>();

        for (int pos = exportStart; pos + 5 < data.Length; pos++)
        {
            int len = RI32(data, pos);
            if (len == int.MinValue)
                continue;

            bool isWide = len < 0;
            int charCount = isWide ? -len : len;
            int byteCount = isWide ? charCount * 2 : charCount;

            if (charCount < 2 || charCount > 1024)
                continue;
            if (pos + 4 + byteCount > data.Length)
                continue;

            string text;
            if (isWide)
            {
                if (data[pos + 4 + byteCount - 2] != 0 || data[pos + 4 + byteCount - 1] != 0)
                    continue;
                text = Encoding.Unicode.GetString(data, pos + 4, byteCount - 2);
            }
            else
            {
                if (data[pos + 4 + byteCount - 1] != 0)
                    continue;
                text = Encoding.UTF8.GetString(data, pos + 4, byteCount - 1);
            }
            if (!IsUsefulText(text))
                continue;

            strings.Add(new FStringEntry(strings.Count, pos, len, byteCount, isWide, text));
            pos += 4 + byteCount - 1;
        }

        return new ParsedFile(exportOffset, exportStart, strings);
    }

    private static bool IsUsefulText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        foreach (char ch in text)
        {
            if (char.IsControl(ch))
                return false;
        }
        return text.Any(char.IsLetter);
    }

    private static int ParseHexOffset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return -1;
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(text[2..], 16)
            : int.Parse(text);
    }

    private static int RI32(byte[] data, int offset)
    {
        return offset + 3 < data.Length ? BitConverter.ToInt32(data, offset) : 0;
    }

    private static long RI64(byte[] data, int offset)
    {
        return offset + 7 < data.Length ? BitConverter.ToInt64(data, offset) : 0;
    }

    private static void WriteI64(byte[] data, int offset, long value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
    }

    private static List<TextPropertyBlock> DetectTextPropertyBlocks(List<FStringEntry> strings)
    {
        var blocks = new List<TextPropertyBlock>();
        for (int i = 0; i + 2 < strings.Count; i += 3)
        {
            var ns = strings[i];
            var key = strings[i + 1];
            var value = strings[i + 2];
            if (!ns.Text.StartsWith("DT_", StringComparison.Ordinal))
                continue;
            if (!key.Text.Contains('_', StringComparison.Ordinal))
                continue;

            int sizeOffset = ns.Offset - 14;
            if (sizeOffset < 0)
                continue;

            blocks.Add(new TextPropertyBlock(sizeOffset, new[] { ns.Index, key.Index, value.Index }));
        }
        return blocks;
    }

    private static Dictionary<int, string> BuildRoles(List<TextPropertyBlock> blocks)
    {
        var roles = new Dictionary<int, string>();
        foreach (var block in blocks)
        {
            roles[block.StringIndexes[0]] = "namespace";
            roles[block.StringIndexes[1]] = "key";
            roles[block.StringIndexes[2]] = "value";
        }
        return roles;
    }

    private static bool NeedsWideEncoding(string text)
    {
        return text.Any(ch => ch > 0x7F);
    }

    private static bool IsTechnicalValue(string text)
    {
        if (text.StartsWith("CONFIG_", StringComparison.Ordinal))
            return true;
        if (!text.Contains('_', StringComparison.Ordinal))
            return false;

        bool hasLetter = false;
        foreach (char ch in text)
        {
            if (ch is '_' or '-' or '.')
                continue;
            if (char.IsDigit(ch))
                continue;
            if (ch >= 'A' && ch <= 'Z')
            {
                hasLetter = true;
                continue;
            }
            return false;
        }

        return hasLetter;
    }

    private static bool IsLooseEditableText(string text)
    {
        if (!IsUsefulText(text))
            return false;
        if (IsTechnicalValue(text))
            return false;
        if (text.StartsWith("DT_", StringComparison.Ordinal))
            return false;
        if (text.Length == 32 && text.All(IsHexDigit))
            return false;
        return true;
    }

    private static bool IsHexDigit(char ch)
    {
        return (ch >= '0' && ch <= '9')
            || (ch >= 'a' && ch <= 'f')
            || (ch >= 'A' && ch <= 'F');
    }

    private readonly record struct FStringEntry(int Index, int Offset, int Length, int ByteCount, bool IsWide, string Text);
    private readonly record struct ParsedFile(int ExportOffset, int ExportStart, List<FStringEntry> Strings);
    private readonly record struct TextPropertyBlock(int SizeOffset, int[] StringIndexes);

    private readonly record struct EncodedFString(string Text, int Length, int ByteCount, bool IsWide, byte[] Bytes)
    {
        public static EncodedFString From(FStringEntry source, string text)
        {
            bool isWide = source.IsWide || NeedsWideEncoding(text);
            if (isWide)
            {
                byte[] bytes = Encoding.Unicode.GetBytes(text);
                int charCount = text.Length + 1;
                return new EncodedFString(text, -charCount, charCount * 2, true, bytes);
            }

            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            return new EncodedFString(text, utf8.Length + 1, utf8.Length + 1, false, utf8);
        }
    }
}


