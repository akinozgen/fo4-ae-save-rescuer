using System.Buffers.Binary;

namespace SaveRescuer.Core;

public sealed class SaveFormatException(string message) : Exception(message);

/// <summary>
/// A parsed Fallout 4 save (.fos). Sections the tool does not need to understand are kept as raw
/// bytes, so <see cref="Rebuild"/> reproduces an untouched save byte for byte.
///
/// Layout in file order:
///   magic | headerSize | header | screenshot | formVersion | gameVersion |
///   pluginInfoSize | pluginBlock | FLT | GD1 | GD2 | ChangeForms | GD3 |
///   formIDArray | visitedWorldspaces | unknownTable3
/// </summary>
public sealed class SaveFile
{
    public const string Magic = "FO4_SAVEGAME";

    /// <summary>The bytes this instance was parsed from; edits do not touch it.</summary>
    public byte[] Raw { get; }
    public string SourcePath { get; }

    // --- header ---
    /// <summary>The header block verbatim. Rewritten as a whole when the location changes.</summary>
    public byte[] Header { get; private set; } = [];
    public uint SaveVersion { get; private set; }
    public uint SaveNumber { get; private set; }
    public string PlayerName { get; private set; } = "";
    public uint PlayerLevel { get; private set; }
    public string Location { get; private set; } = "";
    public string PlayTime { get; private set; } = "";
    public string Race { get; private set; } = "";
    public ushort Sex { get; private set; }
    public uint ShotWidth { get; private set; }
    public uint ShotHeight { get; private set; }
    /// <summary>Fields newer game versions appended after the screenshot size.</summary>
    public int HeaderTailLength { get; private set; }

    public byte[] Screenshot { get; private set; } = [];
    public byte FormVersion { get; set; }
    public string GameVersion { get; private set; } = "";

    // --- plugin block ---
    public List<string> Plugins { get; private set; } = [];
    public List<string> LightPlugins { get; private set; } = [];
    /// <summary>
    /// Whether the file carries the light-plugin count field. It must be preserved even when the
    /// list is empty: readers that expect it would otherwise take the next bytes for a count.
    /// </summary>
    public bool HasLightSection { get; private set; }
    public byte[] PluginBlockTail { get; private set; } = [];

    // --- file location table ---
    public uint Gd1Count { get; private set; }
    public uint Gd2Count { get; private set; }
    public uint Gd3Count { get; private set; }
    public uint ChangeFormCount { get; set; }
    public uint[] FltUnused { get; private set; } = [];

    public byte[] Gd1 { get; private set; } = [];
    public byte[] Gd2 { get; private set; } = [];
    public byte[] ChangeForms { get; set; } = [];
    public byte[] Gd3 { get; private set; } = [];
    public byte[] Unknown3 { get; private set; } = [];

    public List<uint> FormIds { get; set; } = [];
    public List<uint> Visited { get; private set; } = [];

    // offsets kept from parsing, used by the layout check
    private int _afterFlt;
    private int _gd1Offset;
    private int _formIdOffset;
    private int _unknown3Offset;
    private int _visitedEnd;

    private SaveFile(string path, byte[] raw)
    {
        SourcePath = path;
        Raw = raw;
        Parse();
    }

    public static SaveFile Load(string path) => new(path, File.ReadAllBytes(path));
    public static SaveFile FromBytes(byte[] raw, string path = "") => new(path, raw);

    // ---------- readers ----------
    private ushort U16(int p) => BinaryPrimitives.ReadUInt16LittleEndian(Raw.AsSpan(p, 2));
    private uint U32(int p) => BinaryPrimitives.ReadUInt32LittleEndian(Raw.AsSpan(p, 4));

    private string WStr(ref int p)
    {
        var n = U16(p);
        p += 2;
        var s = Win1252.GetString(Raw.AsSpan(p, n));
        p += n;
        return s;
    }

    internal static byte[] PackWStr(string s)
    {
        var body = Win1252.GetBytes(s);
        var buffer = new byte[2 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)body.Length);
        body.CopyTo(buffer, 2);
        return buffer;
    }

    // ---------- parsing ----------
    private void Parse()
    {
        if (Raw.Length < 32 || Win1252.GetString(Raw.AsSpan(0, 12)) != Magic)
            throw new SaveFormatException("Not a Fallout 4 save: the FO4_SAVEGAME marker is missing.");

        var headerSize = (int)U32(12);
        if (16 + headerSize > Raw.Length) throw new SaveFormatException("Header runs past the end of the file.");
        Header = Raw[16..(16 + headerSize)];

        // The header is parsed separately because the screenshot size lives inside it.
        var q = 0;
        SaveVersion = HU32(ref q);
        SaveNumber = HU32(ref q);
        PlayerName = HWStr(ref q);
        PlayerLevel = HU32(ref q);
        Location = HWStr(ref q);
        PlayTime = HWStr(ref q);
        Race = HWStr(ref q);
        Sex = HU16(ref q);
        q += 4 + 4 + 8; // experience, experience to next level, filetime
        ShotWidth = HU32(ref q);
        ShotHeight = HU32(ref q);
        HeaderTailLength = Header.Length - q;

        var p = 16 + headerSize;
        var shotLength = checked((int)(ShotWidth * ShotHeight * 4));
        if (p + shotLength > Raw.Length) throw new SaveFormatException("Screenshot runs past the end of the file.");
        Screenshot = Raw[p..(p + shotLength)];
        p += shotLength;

        FormVersion = Raw[p++];
        GameVersion = WStr(ref p);

        var pluginInfoSize = (int)U32(p);
        p += 4;
        var blockStart = p;
        var count = Raw[p++];
        Plugins = new List<string>(count);
        for (var i = 0; i < count; i++) Plugins.Add(WStr(ref p));

        LightPlugins = [];
        HasLightSection = blockStart + pluginInfoSize - p >= 2;
        if (HasLightSection)
        {
            var lightCount = U16(p);
            p += 2;
            for (var i = 0; i < lightCount; i++) LightPlugins.Add(WStr(ref p));
        }

        var blockEnd = blockStart + pluginInfoSize;
        if (blockEnd > Raw.Length || p > blockEnd)
            throw new SaveFormatException("Plugin block size does not match its contents.");
        PluginBlockTail = Raw[p..blockEnd];
        p = blockEnd;

        if (p + 25 * 4 > Raw.Length) throw new SaveFormatException("File location table is truncated.");
        _formIdOffset = (int)U32(p);
        _unknown3Offset = (int)U32(p + 4);
        _gd1Offset = (int)U32(p + 8);
        var gd2Offset = (int)U32(p + 12);
        var cfOffset = (int)U32(p + 16);
        var gd3Offset = (int)U32(p + 20);
        Gd1Count = U32(p + 24);
        Gd2Count = U32(p + 28);
        Gd3Count = U32(p + 32);
        ChangeFormCount = U32(p + 36);
        FltUnused = new uint[15];
        for (var i = 0; i < 15; i++) FltUnused[i] = U32(p + 40 + i * 4);
        p += 25 * 4;
        _afterFlt = p;

        foreach (var (name, offset) in new[]
                 {
                     ("GD1", _gd1Offset), ("GD2", gd2Offset), ("ChangeForms", cfOffset),
                     ("GD3", gd3Offset), ("formID array", _formIdOffset), ("unknownTable3", _unknown3Offset)
                 })
        {
            if (offset < 0 || offset > Raw.Length)
                throw new SaveFormatException($"{name} offset ({offset}) is outside the file.");
        }

        // Section bounds come from the offset table; each section ends where the next begins.
        Gd1 = Raw[_gd1Offset..gd2Offset];
        Gd2 = Raw[gd2Offset..cfOffset];
        ChangeForms = Raw[cfOffset..gd3Offset];
        Gd3 = Raw[gd3Offset.._formIdOffset];

        var fp = _formIdOffset;
        FormIds = ReadUInt32List(ref fp);
        Visited = ReadUInt32List(ref fp);
        _visitedEnd = fp;
        Unknown3 = Raw[_unknown3Offset..];
    }

    private List<uint> ReadUInt32List(ref int p)
    {
        var n = (int)U32(p);
        p += 4;
        if (n < 0 || p + 4L * n > Raw.Length) throw new SaveFormatException("A form ID table is truncated.");
        var list = new List<uint>(n);
        for (var i = 0; i < n; i++) list.Add(U32(p + i * 4));
        p += 4 * n;
        return list;
    }

    private uint HU32(ref int q)
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(Header.AsSpan(q, 4));
        q += 4;
        return v;
    }

    private ushort HU16(ref int q)
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(Header.AsSpan(q, 2));
        q += 2;
        return v;
    }

    private string HWStr(ref int q)
    {
        var n = BinaryPrimitives.ReadUInt16LittleEndian(Header.AsSpan(q, 2));
        var s = Win1252.GetString(Header.AsSpan(q + 2, n));
        q += 2 + n;
        return s;
    }

    /// <summary>
    /// Replaces the location text, which is the name the in-game load menu shows for this save.
    /// The header is rebuilt around the new string so its length stays consistent.
    /// </summary>
    public void SetLocation(string text)
    {
        var q = 4 + 4;                                                  // version, save number
        q += 2 + BinaryPrimitives.ReadUInt16LittleEndian(Header.AsSpan(q, 2)); // player name
        q += 4;                                                         // level
        var start = q;
        var end = q + 2 + BinaryPrimitives.ReadUInt16LittleEndian(Header.AsSpan(q, 2));

        var packed = PackWStr(text);
        var rebuilt = new byte[start + packed.Length + (Header.Length - end)];
        Header.AsSpan(0, start).CopyTo(rebuilt);
        packed.CopyTo(rebuilt, start);
        Header.AsSpan(end).CopyTo(rebuilt.AsSpan(start + packed.Length));
        Header = rebuilt;
        Location = text;
    }

    /// <summary>
    /// Swaps in pruned plugin lists. The light section keeps existing even when its list empties,
    /// because dropping the count field would shift everything that follows for other readers.
    /// </summary>
    public void ReplacePluginLists(List<string> regular, List<string> light)
    {
        if (regular.Count > 255)
            throw new SaveFormatException($"A save cannot hold {regular.Count} regular plugins.");
        Plugins = regular;
        LightPlugins = light;
    }

    /// <summary>Checks that the parsed sections cover the file with no gaps.</summary>
    public List<string> LayoutIssues()
    {
        var issues = new List<string>();
        if (_afterFlt != _gd1Offset)
            issues.Add($"the table ends at {_afterFlt} but GD1 starts at {_gd1Offset}");
        if (_visitedEnd != _unknown3Offset)
            issues.Add($"visited worldspaces end at {_visitedEnd} but the next section starts at {_unknown3Offset}");
        if (_unknown3Offset > Raw.Length)
            issues.Add("the last section starts past the end of the file");
        return issues;
    }

    /// <summary>Writes the save back out from its parsed parts, recomputing the offset table.</summary>
    public byte[] Rebuild()
    {
        var block = new List<byte>(64 + Plugins.Count * 32);
        block.Add((byte)Plugins.Count);
        foreach (var name in Plugins) block.AddRange(PackWStr(name));
        if (HasLightSection)
        {
            block.AddRange(BitConverter.GetBytes((ushort)LightPlugins.Count));
            foreach (var name in LightPlugins) block.AddRange(PackWStr(name));
        }
        block.AddRange(PluginBlockTail);

        var fltAt = 12 + 4 + Header.Length + Screenshot.Length + 1
                    + 2 + Win1252.GetBytes(GameVersion).Length + 4 + block.Count;
        var gd1 = fltAt + 25 * 4;
        var gd2 = gd1 + Gd1.Length;
        var cf = gd2 + Gd2.Length;
        var gd3 = cf + ChangeForms.Length;
        var formId = gd3 + Gd3.Length;
        var visited = formId + 4 + 4 * FormIds.Count;
        var unknown3 = visited + 4 + 4 * Visited.Count;

        var total = unknown3 + Unknown3.Length;
        var output = new byte[total];
        var w = 0;

        void Write(ReadOnlySpan<byte> bytes)
        {
            bytes.CopyTo(output.AsSpan(w));
            w += bytes.Length;
        }

        void WriteU32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(w), value);
            w += 4;
        }

        Write(Win1252.GetBytes(Magic));
        WriteU32((uint)Header.Length);
        Write(Header);
        Write(Screenshot);
        output[w++] = FormVersion;
        Write(PackWStr(GameVersion));
        WriteU32((uint)block.Count);
        Write(block.ToArray());

        WriteU32((uint)formId);
        WriteU32((uint)unknown3);
        WriteU32((uint)gd1);
        WriteU32((uint)gd2);
        WriteU32((uint)cf);
        WriteU32((uint)gd3);
        WriteU32(Gd1Count);
        WriteU32(Gd2Count);
        WriteU32(Gd3Count);
        WriteU32(ChangeFormCount);
        foreach (var value in FltUnused) WriteU32(value);

        Write(Gd1);
        Write(Gd2);
        Write(ChangeForms);
        Write(Gd3);
        WriteU32((uint)FormIds.Count);
        foreach (var id in FormIds) WriteU32(id);
        WriteU32((uint)Visited.Count);
        foreach (var id in Visited) WriteU32(id);
        Write(Unknown3);

        if (w != total) throw new SaveFormatException($"Rebuild wrote {w} bytes, expected {total}.");
        return output;
    }
}
