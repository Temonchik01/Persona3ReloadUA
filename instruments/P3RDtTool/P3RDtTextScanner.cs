using System.Text;
using System.Xml.Linq;

namespace P3RDtTool;

public static class P3RDtTextScanner
{
    public static void Scan(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var parsed = ParseTextBlocks(data);

        Console.WriteLine($"=== {Path.GetFileName(path)} ({data.Length} bytes) ===");
        Console.WriteLine($"Names: {parsed.Names.Count}, ExportData: 0x{parsed.ExportStart:X}");
        Console.WriteLine();
        Console.WriteLine("Index  TextOffset  Len  A                         B                         C                         Text");
        Console.WriteLine(new string('-', 125));

        foreach (var block in parsed.Blocks)
        {
            Console.WriteLine(
                $"{block.Index,5}  0x{block.TextOffset:X6}  {block.Length,3}  " +
                $"{Trim(block.A),-25} {Trim(block.B),-25} {Trim(block.C),-25} {block.Text}");
        }

        Console.WriteLine();
        Console.WriteLine($"Done. Found {parsed.Blocks.Count} text blocks.");
    }

    public static void ExportXml(string inputPath, string outputPath)
    {
        byte[] data = File.ReadAllBytes(inputPath);
        var parsed = ParseTextBlocks(data);

        var doc = new XDocument(
            new XElement("P3RDataTableText",
                new XAttribute("source", Path.GetFileName(inputPath)),
                new XAttribute("textBlockCount", parsed.Blocks.Count),
                parsed.Blocks.Select(block =>
                    new XElement("Text",
                        new XAttribute("index", block.Index),
                        new XAttribute("offset", $"0x{block.TextOffset:X}"),
                        new XAttribute("length", block.Length),
                        new XAttribute("role", IsEditableProperty(data, parsed.Names, block) ? "value" : "technical"),
                        new XAttribute("property", PropertyName(data, parsed.Names, block)),
                        new XAttribute("a", block.A),
                        new XAttribute("b", block.B),
                        new XAttribute("c", block.C),
                        new XElement("Source", block.Text),
                        new XElement("Translation", block.Text)))));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        doc.Save(outputPath);
        Console.WriteLine($"Exported {parsed.Blocks.Count} text blocks -> {outputPath}");
    }

    public static void ImportXml(string inputPath, string xmlPath, string outputPath)
    {
        byte[] data = File.ReadAllBytes(inputPath);
        var parsed = ParseTextBlocks(data);
        var doc = XDocument.Load(xmlPath);

        var translations = doc.Root?
            .Elements("Text")
            .Select(e => new
            {
                Index = (int?)e.Attribute("index") ?? -1,
                Offset = ParseHexOffset((string?)e.Attribute("offset")),
                Source = (string?)e.Element("Source") ?? "",
                Translation = (string?)e.Element("Translation") ?? ""
            })
            .ToDictionary(x => x.Offset);

        if (translations is null || doc.Root?.Name != "P3RDataTableText")
            throw new InvalidDataException("Missing P3RDataTableText XML root.");
        var knownOffsets = parsed.Blocks.Select(block => block.TextOffset).ToHashSet();
        if (translations.Keys.Any(offset => !knownOffsets.Contains(offset)))
            throw new InvalidDataException("XML contains an offset that is not a recognized source string.");

        using var ms = new MemoryStream(data.Length);
        int cursor = 0;
        int changed = 0;
        var containers = FindContainers(data, parsed);

        foreach (var block in parsed.Blocks)
        {
            if (!translations.TryGetValue(block.TextOffset, out var entry))
            {
                // Old exports omitted UTF-16 and some legitimate tagged strings.
                // Preserve omitted values; match legacy entries by original offset.
                continue; // Unspecified values remain byte-identical (legacy XML omissions).
            }
            if (entry.Offset != block.TextOffset)
                throw new InvalidDataException($"Offset mismatch at index {block.Index}: XML 0x{entry.Offset:X}, file 0x{block.TextOffset:X}.");
            if (entry.Source != block.Text)
                throw new InvalidDataException(
                    $"Source mismatch at index {block.Index} / 0x{block.TextOffset:X}. " +
                    "Do not edit <Source>; put translated text in <Translation>.");

            string replacement = string.IsNullOrEmpty(entry.Translation) ? entry.Source : entry.Translation;
            bool edited = replacement != block.Text;
            ValidateEdit(data, parsed, block, replacement);
            int oldEnd = checked(block.TextOffset + 4 + block.ByteLength);
            if (!edited)
            {
                // Preserve original encoding and all surrounding bytes for no-op imports.
                ms.Write(data, cursor, oldEnd - cursor);
                cursor = oldEnd;
                continue;
            }

            bool unicode = replacement.Any(ch => ch > 0x7f);
            byte[] encoded = unicode
                ? Encoding.Unicode.GetBytes(replacement + "\0")
                : Encoding.ASCII.GetBytes(replacement + "\0");
            int newLength = unicode ? -(replacement.Length + 1) : encoded.Length;
            // UE4 tagged StrProperty: FName, type FName, Size(int32),
            // ArrayIndex(int32), HasPropertyGuid(byte), then FString.
            // Update Size before copying the tag; offsets still refer to the original.
            WriteI32(data, block.BlockStart + 16, checked(RI32(data, block.BlockStart + 16) + encoded.Length - block.ByteLength));
            int delta = encoded.Length - block.ByteLength;
            foreach (var container in containers)
                if (container.Start <= block.BlockStart && oldEnd <= container.End)
                    WriteI32(data, container.SizeOffset, checked(RI32(data, container.SizeOffset) + delta));
            ms.Write(data, cursor, block.TextOffset - cursor);
            ms.Write(BitConverter.GetBytes(newLength));
            ms.Write(encoded);

            cursor = oldEnd;
            if (replacement != block.Text)
                changed++;
        }

        ms.Write(data, cursor, data.Length - cursor);
        byte[] output = ms.ToArray();
        // Container tags precede the first edited child and may have already
        // been copied to the output stream. Their offsets stay unchanged only
        // until an earlier edit; account for every preceding string delta.
        foreach (var container in containers)
        {
            int shift = 0;
            foreach (var block in parsed.Blocks)
            {
                if (block.TextOffset >= container.SizeOffset) break;
                if (!translations.TryGetValue(block.TextOffset, out var entry)) continue;
                string value = string.IsNullOrEmpty(entry.Translation) ? entry.Source : entry.Translation;
                if (value == block.Text) continue;
                int bytes = value.Any(ch => ch > 0x7f) ? checked((value.Length + 1) * 2) : value.Length + 1;
                shift += bytes - block.ByteLength;
            }
            WriteI32(output, container.SizeOffset + shift, RI32(data, container.SizeOffset));
        }
        if (changed > 0)
        {
            int exportCount = (RI32(data, 0x30) - parsed.Header.ExportOffset) / 72;
            if (exportCount != 1)
                throw new InvalidDataException("Text import currently supports exactly one Zen export.");
            long newExportSize = output.Length - parsed.ExportStart;
            WriteI64(output, parsed.Header.ExportOffset + 8, newExportSize);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, output);
        Console.WriteLine($"Imported {parsed.Blocks.Count} text blocks ({changed} changed) -> {outputPath}");
    }

    public static int CountChanges(string inputPath, string xmlPath)
    {
        byte[] data = File.ReadAllBytes(inputPath);
        var parsed = ParseTextBlocks(data);
        var doc = XDocument.Load(xmlPath);

        var translations = doc.Root?
            .Elements("Text")
            .Select(e => new
            {
                Index = (int?)e.Attribute("index") ?? -1,
                Offset = ParseHexOffset((string?)e.Attribute("offset")),
                Source = (string?)e.Element("Source") ?? "",
                Translation = (string?)e.Element("Translation") ?? ""
            })
            .ToDictionary(x => x.Offset);

        if (translations is null || doc.Root?.Name != "P3RDataTableText")
            throw new InvalidDataException("Missing P3RDataTableText XML root.");
        var knownOffsets = parsed.Blocks.Select(block => block.TextOffset).ToHashSet();
        if (translations.Keys.Any(offset => !knownOffsets.Contains(offset)))
            throw new InvalidDataException("XML contains an offset that is not a recognized source string.");

        int changed = 0;
        foreach (var block in parsed.Blocks)
        {
            if (!translations.TryGetValue(block.TextOffset, out var entry))
            {
                // Old exports omitted UTF-16 and some legitimate tagged strings.
                // Preserve omitted values; match legacy entries by original offset.
                continue; // Unspecified values remain byte-identical (legacy XML omissions).
            }
            if (entry.Offset != block.TextOffset)
                throw new InvalidDataException($"Offset mismatch at index {block.Index}: XML 0x{entry.Offset:X}, file 0x{block.TextOffset:X}.");
            if (entry.Source != block.Text)
                throw new InvalidDataException(
                    $"Source mismatch at index {block.Index} / 0x{block.TextOffset:X}. " +
                    "Do not edit <Source>; put translated text in <Translation>.");

            string replacement = string.IsNullOrEmpty(entry.Translation) ? entry.Source : entry.Translation;
            ValidateEdit(data, parsed, block, replacement);
            if (replacement != block.Text)
                changed++;
        }

        return changed;
    }
    private static ParsedTable ParseTextBlocks(byte[] data)
    {
        if (data.Length < 64)
            throw new InvalidDataException("Truncated Zen package header.");
        var header = ReadHeader(data);
        if (header.NameMapOffset < 64 || header.ExportOffset < 64 ||
            header.ExportOffset + 72L > data.Length || header.GraphDataOffset < 64 ||
            header.GraphDataSize < 0 || header.GraphDataOffset + (long)header.GraphDataSize > data.Length)
            throw new InvalidDataException("Invalid Zen package offsets.");
        var names = ParseNames(data, header.NameMapOffset, out _);
        int exportStart = header.GraphDataOffset + header.GraphDataSize;
        var blocks = new List<TextBlock>();

        for (int pos = exportStart; pos + 29 < data.Length; pos++)
        {
            if (!TryReadTextBlock(data, names, blocks.Count, pos, out var block))
                continue;

            blocks.Add(block);
            pos = block.TextOffset + 4 + block.ByteLength - 1;
        }

        return new ParsedTable(header, names, exportStart, blocks);
    }

    private static bool TryReadTextBlock(byte[] data, List<string> names, int index, int pos, out TextBlock block)
    {
        block = default;

        int a = RI32(data, pos);
        int anum = RI32(data, pos + 4);
        int b = RI32(data, pos + 8);
        int bnum = RI32(data, pos + 12);
        int c = RI32(data, pos + 16);
        int cnum = RI32(data, pos + 20);
        byte marker = data[pos + 24];
        int textOffset = pos + 25;
        int len = RI32(data, textOffset);

        if (a >= 0 && a + 1 < names.Count && b >= 0 && b + 1 < names.Count &&
            names[b + 1] == "TextProperty" && anum == 0 && bnum == 0 && cnum == 0 && marker == 0 &&
            c > 5 && textOffset + (long)c <= data.Length)
        {
            int end = textOffset + c;
            // FText flags (4), history Base (1), namespace, key, source string.
            if (data[textOffset + 4] != 0) return false;
            int cursor = textOffset + 5;
            if (!ReadFTextString(data, ref cursor, end, out _, out _) ||
                !ReadFTextString(data, ref cursor, end, out _, out _)) return false;
            int valueOffset = cursor;
            if (!ReadFTextString(data, ref cursor, end, out string value, out int valueLength) || cursor != end || valueLength == 0) return false;
            block = new TextBlock(index, pos, valueOffset, valueLength,
                FormatName(names, a), FormatName(names, b), FormatName(names, c), value, true);
            return true;
        }

        if (!IsName(names, a) || !IsName(names, b))
            return false;
        if (anum != 0 || bnum != 0 || cnum != 0)
            return false;
        if (marker != 0)
            return false;
        if (len == int.MinValue || Math.Abs(len) < 2 || Math.Abs(len) > 16384)
            return false;
        int bytes = checked(Math.Abs(len) * (len < 0 ? 2 : 1));
        if (textOffset + 4L + bytes > data.Length)
            return false;
        if (data[textOffset + 4 + bytes - 1] != 0 ||
            (len < 0 && data[textOffset + 4 + bytes - 2] != 0))
            return false;

        bool taggedString = b + 1 < names.Count && names[b + 1] == "StrProperty" && c == 4 + bytes;
        if (!IsName(names, c) && !taggedString)
            return false;

        string text;
        try
        {
            text = len < 0
                ? new UnicodeEncoding(false, false, true).GetString(data, textOffset + 4, bytes - 2)
                : new UTF8Encoding(false, true).GetString(data, textOffset + 4, bytes - 1);
        }
        catch (DecoderFallbackException) { return false; }
        if (!IsUsefulText(text, taggedString))
            return false;

        block = new TextBlock(
            index,
            pos,
            textOffset,
            len,
            FormatName(names, a),
            FormatName(names, b),
            FormatName(names, c),
            text);
        return true;
    }

    private static bool ReadFTextString(byte[] data, ref int cursor, int end, out string value, out int length)
    {
        value = ""; length = 0;
        if (cursor + 4 > end) return false;
        length = RI32(data, cursor); cursor += 4;
        if (length == int.MinValue || Math.Abs(length) > 16384) return false;
        int bytes = Math.Abs(length) * (length < 0 ? 2 : 1);
        if (cursor + bytes > end) return false;
        if (bytes == 0) return true;
        int nul = length < 0 ? 2 : 1;
        if (data[cursor + bytes - 1] != 0 || (nul == 2 && data[cursor + bytes - 2] != 0)) return false;
        try { value = (length < 0 ? (Encoding)new UnicodeEncoding(false, false, true) : new UTF8Encoding(false, true)).GetString(data, cursor, bytes - nul); }
        catch (DecoderFallbackException) { return false; }
        cursor += bytes; return true;
    }

    private readonly record struct ContainerTag(int Start, int End, int SizeOffset);

    private static List<ContainerTag> FindContainers(byte[] data, ParsedTable table)
    {
        var result = new List<ContainerTag>();
        for (int pos = table.ExportStart; pos + 49 <= data.Length; pos++)
        {
            int name = RI32(data, pos), type = RI32(data, pos + 8);
            if (name < 0 || name + 1 >= table.Names.Count || type < 0 || type + 1 >= table.Names.Count ||
                RI32(data, pos + 4) != 0 || RI32(data, pos + 12) != 0 || RI32(data, pos + 20) != 0) continue;
            string kind = table.Names[type + 1];
            int extra = kind == "ArrayProperty" ? 8 : kind == "StructProperty" ? 24 : -1;
            if (extra < 0) continue;
            int payload = pos + 25 + extra;
            int size = RI32(data, pos + 16);
            if (payload > data.Length || data[payload - 1] != 0 || size <= 0 || payload + (long)size > data.Length) continue;
            int inner = RI32(data, pos + 24);
            if (inner < 0 || inner + 1 >= table.Names.Count) continue;
            result.Add(new ContainerTag(payload, payload + size, pos + 16));
        }
        return result;
    }

    private static bool IsUsefulText(string text, bool taggedString)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (char ch in text)
        {
            if (char.IsControl(ch) && !(taggedString && ch is '\n' or '\r' or '\t'))
                return false;
        }

        return taggedString || text.Any(char.IsLetter);
    }

    private static Header ReadHeader(byte[] data)
    {
        return new Header(
            RI32(data, 0x18),
            RI32(data, 0x2C),
            RI32(data, 0x34),
            RI32(data, 0x38));
    }

    private static List<string> ParseNames(byte[] data, int nameOffset, out int tableEnd)
    {
        var names = new List<string> { "" };
        int pos = nameOffset + 1;

        while (pos < data.Length)
        {
            byte len = data[pos];
            if (len == 0)
            {
                pos++;
                break;
            }

            if (len > 200 || pos + 1 + len >= data.Length)
                break;

            names.Add(Encoding.UTF8.GetString(data, pos + 1, len));
            pos += 1 + len + 1;
        }

        tableEnd = pos;
        return names;
    }

    private static string PropertyName(byte[] data, List<string> names, TextBlock block)
    {
        int index = RI32(data, block.BlockStart);
        return index >= 0 && index + 1 < names.Count ? names[index + 1] : "";
    }

    private static bool IsEditableProperty(byte[] data, List<string> names, TextBlock block)
        => IsStringProperty(data, names, block) && PropertyName(data, names, block) is not ("TextLabel" or "Comment" or "Font");

    private static bool IsStringProperty(byte[] data, List<string> names, TextBlock block)
    {
        int type = RI32(data, block.BlockStart + 8);
        if (block.IsFText)
            return type >= 0 && type + 1 < names.Count && names[type + 1] == "TextProperty";
        // The historical XML scanner prepends a synthetic empty name, so its
        // name list is shifted by one relative to serialized FName indices.
        // Keep historical XML indices/offsets compatible, resolve the real type here.
        return type >= 0 && type + 1 < names.Count && names[type + 1] == "StrProperty" &&
            RI32(data, block.TextOffset - 13) == 0 &&
            RI32(data, block.TextOffset - 9) == 4 + block.ByteLength &&
            RI32(data, block.TextOffset - 5) == 0 && data[block.TextOffset - 1] == 0;
    }

    private static void ValidateEdit(byte[] data, ParsedTable table, TextBlock block, string replacement)
    {
        if (replacement == block.Text) return;
        if (replacement.IndexOf('\0') >= 0 || replacement.Length > 16383)
            throw new InvalidDataException($"Invalid text at index {block.Index}.");
        if (!IsEditableProperty(data, table.Names, block))
            throw new InvalidDataException($"Unsafe text edit at index {block.Index} / 0x{block.TextOffset:X}: not a supported tagged StrProperty.");
    }

    private static void WriteI32(byte[] data, int offset, int value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, data, offset, 4);
    }

    private static string FormatName(List<string> names, int index)
    {
        if (!IsName(names, index)) return $"<Size:{index}>";
        string name = names[index];
        return string.IsNullOrEmpty(name) ? "<None0>" : name;
    }

    private static string Trim(string text)
    {
        const int max = 25;
        return text.Length <= max ? text : text[..(max - 1)] + "~";
    }

    private static bool IsName(List<string> names, int index)
    {
        return index >= 0 && index < names.Count;
    }

    private static int RI32(byte[] data, int offset)
    {
        return offset + 3 < data.Length ? BitConverter.ToInt32(data, offset) : 0;
    }

    private static int ParseHexOffset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return -1;

        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(text[2..], 16)
            : int.Parse(text);
    }

    private static void WriteI64(byte[] data, int offset, long value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
    }

    private readonly record struct Header(int NameMapOffset, int ExportOffset, int GraphDataOffset, int GraphDataSize);
    private readonly record struct TextBlock(int Index, int BlockStart, int TextOffset, int Length, string A, string B, string C, string Text, bool IsFText = false)
    {
        public int ByteLength => checked(Math.Abs(Length) * (Length < 0 ? 2 : 1));
    }
    private readonly record struct ParsedTable(Header Header, List<string> Names, int ExportStart, List<TextBlock> Blocks);
}


