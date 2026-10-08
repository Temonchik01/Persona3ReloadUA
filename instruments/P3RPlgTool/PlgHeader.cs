using System.Text;

namespace P3RPlgTool;

static class PlgHeader
{
    public static (List<string> Names, int ExportStart, int ExportOffset) Read(byte[] data)
    {
        int nameMapOffset = RI32(data, 0x18);
        int exportOffset  = RI32(data, 0x2C);
        int graphDataOff  = RI32(data, 0x34);
        int graphDataSize = RI32(data, 0x38);
        int exportStart   = graphDataOff + graphDataSize;

        var names = ParseNames(data, nameMapOffset, out _);
        return (names, exportStart, exportOffset);
    }

    static List<string> ParseNames(byte[] data, int nameOffset, out int tableEnd)
    {
        var names = new List<string> { "" };
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

    public static int RI32(byte[] d, int o) => o + 3 < d.Length ? BitConverter.ToInt32(d, o) : 0;
}
