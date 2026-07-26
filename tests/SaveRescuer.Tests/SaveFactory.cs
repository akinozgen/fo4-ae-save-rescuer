using System.Text;

namespace SaveRescuer.Tests;

/// <summary>Builds synthetic .fos files so the parser can be tested without game data.</summary>
internal static class SaveFactory
{
    public static string Write(
        string dir,
        string characterName = "TestChar",
        uint level = 42,
        string location = "Commonwealth",
        string playTime = "0d.1h.0m",
        string gameVersion = "1.11.221.0",
        IEnumerable<string>? plugins = null,
        IEnumerable<string>? lightPlugins = null,
        int shotWidth = 2,
        int shotHeight = 2,
        int headerPadding = 0)
    {
        plugins ??= ["Fallout4.esm", "Mod.esp"];
        lightPlugins ??= [];

        var path = Path.Combine(dir, $"Save1_{characterName}.fos");
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.Latin1);

        bw.Write(Encoding.ASCII.GetBytes("FO4_SAVEGAME"));

        // header assembled separately so its size can be written first
        using var hs = new MemoryStream();
        using var hw = new BinaryWriter(hs, Encoding.Latin1);
        hw.Write(11u);                       // save version
        hw.Write(7u);                        // save number
        WStr(hw, characterName);
        hw.Write(level);
        WStr(hw, location);
        WStr(hw, playTime);
        WStr(hw, "HumanRace");
        hw.Write((ushort)0);                 // sex
        hw.Write(1.0f);                      // current exp
        hw.Write(2.0f);                      // exp to level
        hw.Write(DateTime.UtcNow.ToFileTime());
        hw.Write(shotWidth);
        hw.Write(shotHeight);
        if (headerPadding > 0) hw.Write(new byte[headerPadding]);   // simulate newer game padding

        var header = hs.ToArray();
        bw.Write((uint)header.Length);
        bw.Write(header);

        bw.Write(new byte[shotWidth * shotHeight * 4]);             // screenshot (RGBA)
        bw.Write((byte)69);                                        // form version
        WStr(bw, gameVersion);

        // plugin block
        using var ps = new MemoryStream();
        using var pw = new BinaryWriter(ps, Encoding.Latin1);
        var pl = plugins.ToList();
        pw.Write((byte)pl.Count);
        foreach (var p in pl) WStr(pw, p);
        var lt = lightPlugins.ToList();
        if (lt.Count > 0)
        {
            pw.Write((ushort)lt.Count);
            foreach (var p in lt) WStr(pw, p);
        }
        var block = ps.ToArray();
        bw.Write((uint)block.Length);
        bw.Write(block);

        bw.Write(new byte[64]);                                    // stand-in for the rest of the save
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static void WStr(BinaryWriter bw, string s)
    {
        var bytes = Encoding.Latin1.GetBytes(s);
        bw.Write((ushort)bytes.Length);
        bw.Write(bytes);
    }
}
