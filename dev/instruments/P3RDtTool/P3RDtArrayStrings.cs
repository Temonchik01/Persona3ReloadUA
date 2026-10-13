using System.Text;
using System.Xml.Linq;

namespace P3RDtTool;

// Strict decoder for the name arrays in the supplied DataAssets. A complete
// payload match is required; arbitrary binary strings are never made editable.
public static class P3RDtArrayStrings
{
    private record Entry(int Offset, int End, string Text, bool Value);
    private record Table(int Export, int SizeOffset, int Count, string Layout, List<Entry> Entries);
    private static int I(byte[] b, int p) => BitConverter.ToInt32(b, p);
    private static Table? Parse(byte[] b)
    {
        try
        {
            if (b.Length < 64) return null;
            int p = I(b,24)+1;
            var names = new List<string>();
            while (p < I(b,44) && b[p] != 0)
            {
                int n=b[p++];
                if (p+n>=b.Length || b[p+n]!=0) return null;
                names.Add(new UTF8Encoding(false,true).GetString(b,p,n));p+=n+1;
            }
            int start=checked(I(b,52)+I(b,56));
            if (start<64 || start+37>b.Length || (I(b,48)-I(b,44))/72!=1) return null;
            if (names[I(b,start+8)]!="ArrayProperty" || I(b,start+4)!=0 || I(b,start+12)!=0 || I(b,start+20)!=0 || b[start+32]!=0) return null;
            string inner=names[I(b,start+24)];
            if (inner is not ("StrProperty" or "TextProperty")) return null;
            int end=checked(start+33+I(b,start+16));
            int count=I(b,start+33);p=start+37;
            if(count<0 || count>100000 || end>b.Length || end<p) return null;
            var entries=new List<Entry>();
            for(int j=0;j<count;j++)
            {
                if(inner=="TextProperty")
                {
                    if(p+5>end || b[p+4]!=0) return null; // FText history Base
                    p+=5;
                    entries.Add(Read(b,ref p,end,false)); // namespace
                    entries.Add(Read(b,ref p,end,false)); // localization key
                }
                entries.Add(Read(b,ref p,end,true));
            }
            if(p!=end) return null;
            return new Table(I(b,44),start+16,count,inner=="TextProperty"?"array-text":"array-str",entries);
        }
        catch(Exception e) when(e is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidDataException) {return null;}
    }
    private static Entry Read(byte[] b,ref int p,int end,bool value)
    {
        int offset=p;
        if(p+4>end) throw new InvalidDataException();
        int n=I(b,p);p+=4;
        if(n==int.MinValue || Math.Abs(n)>16384) throw new InvalidDataException();
        int size=checked(Math.Abs(n)*(n<0?2:1));
        if(p+size>end) throw new InvalidDataException();
        string text="";
        if(size>0)
        {
            int nul=n<0?2:1;
            if(b[p+size-1]!=0 || (nul==2 && b[p+size-2]!=0)) throw new InvalidDataException();
            text=(n<0?(Encoding)new UnicodeEncoding(false,false,true):new UTF8Encoding(false,true)).GetString(b,p,size-nul);
        }
        p+=size;return new Entry(offset,p,text,value);
    }
    public static bool Export(string source,string output)
    {
        var b=File.ReadAllBytes(source);var table=Parse(b);if(table is null)return false;
        var doc=new XDocument(new XElement("P3RDataTableFStrings",new XAttribute("source",Path.GetFileName(source)),new XAttribute("layout",table.Layout),
            table.Entries.Select((e,i)=>(e,i)).Where(x=>x.e.Value).Select(x=>new XElement("String",new XAttribute("index",x.i),new XAttribute("offset",$"0x{x.e.Offset:X}"),new XAttribute("role","value"),new XElement("Source",x.e.Text),new XElement("Translation",x.e.Text)))));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);doc.Save(output);return true;
    }
    public static bool Import(string source,string xml,string? output,out int changes)
    {
        changes=0;var doc=XDocument.Load(xml);string? layout=(string?)doc.Root?.Attribute("layout");
        byte[] b=File.ReadAllBytes(source);var table=Parse(b);
        bool explicitArray = layout is "array-text" or "array-str";
        if(table is null)
        {
            if(explicitArray)throw new InvalidDataException("Source is not a supported complete string array.");
            return false;
        }
        if(doc.Root?.Name!="P3RDataTableFStrings" ||
            (layout != table.Layout && layout is not ("loose-fstrings" or "text-property")))
            throw new InvalidDataException("Array XML layout mismatch.");
        var known=table.Entries.Where(e=>e.Value).ToDictionary(e=>e.Offset);
        var replacements=new Dictionary<int,string>();
        foreach(var node in doc.Root.Elements("String"))
        {
            string offset=(string?)node.Attribute("offset")??"";
            int pos=Convert.ToInt32(offset.StartsWith("0x")?offset[2..]:offset,16);
            if(!known.TryGetValue(pos,out var e) || (string?)node.Element("Source")!=e.Text)throw new InvalidDataException("Unknown array offset or modified Source.");
            string value=(string?)node.Element("Translation")??e.Text;
            if(value.Contains('\0') || value.Length>16383)throw new InvalidDataException("Invalid array translation.");
            replacements.Add(pos,value);
        }
        using var ms=new MemoryStream();int cursor=0;
        foreach(var e in table.Entries)
        {
            if(!replacements.TryGetValue(e.Offset,out string? value) || value==e.Text)continue;
            ms.Write(b,cursor,e.Offset-cursor);
            bool wide=value.Any(c=>c>127);byte[] bytes=(wide?Encoding.Unicode:Encoding.ASCII).GetBytes(value+"\0");
            ms.Write(BitConverter.GetBytes(wide?-(value.Length+1):bytes.Length));ms.Write(bytes);cursor=e.End;changes++;
        }
        ms.Write(b,cursor,b.Length-cursor);byte[] result=ms.ToArray();
        int delta=result.Length-b.Length;
        BitConverter.GetBytes(checked(I(b,table.SizeOffset)+delta)).CopyTo(result,table.SizeOffset);
        BitConverter.GetBytes(BitConverter.ToInt64(b,table.Export+8)+delta).CopyTo(result,table.Export+8);
        if(Parse(result) is null)throw new InvalidDataException("Translated array failed structural validation.");
        if(output is not null){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllBytes(output,result);}
        return true;
    }
}
