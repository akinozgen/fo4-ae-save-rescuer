using System.Buffers.Binary;
using SaveRescuer.Core;

namespace SaveRescuer.Tests;

/// <summary>
/// Builds synthetic .fos files so the parser, the surgeon and the workflow can be exercised
/// without a real save. The layout follows the same order the game writes.
/// </summary>
public sealed class SaveBuilder
{
    public uint SaveVersion { get; set; } = 12;
    public uint SaveNumber { get; set; } = 7;
    public string PlayerName { get; set; } = "Tester";
    public uint Level { get; set; } = 42;
    public string Location { get; set; } = "Sanctuary Hills";
    public string PlayTime { get; set; } = "003.12.45";
    public string Race { get; set; } = "HumanRace";
    public ushort Sex { get; set; } = 1;
    public byte[] HeaderTail { get; set; } = [];

    public uint ShotWidth { get; set; } = 2;
    public uint ShotHeight { get; set; } = 2;

    public byte FormVersion { get; set; } = 69;
    public string GameVersion { get; set; } = "1.10.980";

    public List<string> Plugins { get; set; } = ["Fallout4.esm"];
    public List<string> LightPlugins { get; set; } = [];
    public bool IncludeLightSection { get; set; } = true;
    public byte[] PluginBlockTail { get; set; } = [];

    public byte[] Gd1 { get; set; } = [1, 2, 3, 4];
    public byte[] Gd2 { get; set; } = [5, 6];
    public byte[] Gd3 { get; set; } = [7, 8, 9];
    public byte[] Unknown3 { get; set; } = [0xAA, 0xBB];

    public List<byte[]> ChangeForms { get; set; } = [];
    public List<uint> FormIds { get; set; } = [];
    public List<uint> Visited { get; set; } = [0x0000_0001];

    /// <summary>A single ChangeForm record. <paramref name="lengthKind"/> picks the width of the length fields.</summary>
    public static byte[] ChangeForm(int refType, int refValue, byte[] data, int lengthKind = 0, byte version = 69)
    {
        var record = new List<byte>();
        var refId = ((uint)refType << 22) | (uint)refValue;
        record.Add((byte)(refId >> 16));
        record.Add((byte)(refId >> 8));
        record.Add((byte)refId);
        record.AddRange(BitConverter.GetBytes(0x1234_5678u));   // change flags
        record.Add((byte)(lengthKind << 6));                    // type, with the length width on top
        record.Add(version);

        switch (lengthKind)
        {
            case 0:
                record.Add((byte)data.Length);
                record.Add((byte)data.Length);
                break;
            case 1:
                record.AddRange(BitConverter.GetBytes((ushort)data.Length));
                record.AddRange(BitConverter.GetBytes((ushort)data.Length));
                break;
            default:
                record.AddRange(BitConverter.GetBytes((uint)data.Length));
                record.AddRange(BitConverter.GetBytes((uint)data.Length));
                break;
        }

        record.AddRange(data);
        return record.ToArray();
    }

    /// <summary>Form ID owned by a regular plugin at <paramref name="index"/>.</summary>
    public static uint RegularForm(int index, uint objectId = 0x001234) => ((uint)index << 24) | objectId;

    /// <summary>Form ID owned by the light plugin at <paramref name="index"/>.</summary>
    public static uint LightForm(int index, uint objectId = 0x123) => (0xFEu << 24) | ((uint)index << 12) | objectId;

    /// <summary>Form created during play, owned by no plugin.</summary>
    public static uint CreatedForm(uint objectId = 0x004321) => (0xFFu << 24) | objectId;

    public byte[] Build()
    {
        var header = new List<byte>();
        header.AddRange(BitConverter.GetBytes(SaveVersion));
        header.AddRange(BitConverter.GetBytes(SaveNumber));
        header.AddRange(WStr(PlayerName));
        header.AddRange(BitConverter.GetBytes(Level));
        header.AddRange(WStr(Location));
        header.AddRange(WStr(PlayTime));
        header.AddRange(WStr(Race));
        header.AddRange(BitConverter.GetBytes(Sex));
        header.AddRange(BitConverter.GetBytes(1.5f));            // experience
        header.AddRange(BitConverter.GetBytes(2.5f));            // experience to next level
        header.AddRange(BitConverter.GetBytes(0x0123_4567_89ABL)); // filetime
        header.AddRange(BitConverter.GetBytes(ShotWidth));
        header.AddRange(BitConverter.GetBytes(ShotHeight));
        header.AddRange(HeaderTail);

        var block = new List<byte> { (byte)Plugins.Count };
        foreach (var name in Plugins) block.AddRange(WStr(name));
        if (IncludeLightSection)
        {
            block.AddRange(BitConverter.GetBytes((ushort)LightPlugins.Count));
            foreach (var name in LightPlugins) block.AddRange(WStr(name));
        }
        block.AddRange(PluginBlockTail);

        var screenshot = new byte[ShotWidth * ShotHeight * 4];
        for (var i = 0; i < screenshot.Length; i++) screenshot[i] = (byte)(i * 7 % 251);

        var changeForms = ChangeForms.SelectMany(c => c).ToArray();

        var out_ = new List<byte>();
        out_.AddRange(Win1252.GetBytes("FO4_SAVEGAME"));
        out_.AddRange(BitConverter.GetBytes((uint)header.Count));
        out_.AddRange(header);
        out_.AddRange(screenshot);
        out_.Add(FormVersion);
        out_.AddRange(WStr(GameVersion));
        out_.AddRange(BitConverter.GetBytes((uint)block.Count));
        out_.AddRange(block);

        var fltAt = out_.Count;
        var gd1 = fltAt + 25 * 4;
        var gd2 = gd1 + Gd1.Length;
        var cf = gd2 + Gd2.Length;
        var gd3 = cf + changeForms.Length;
        var formId = gd3 + Gd3.Length;
        var visited = formId + 4 + 4 * FormIds.Count;
        var unknown3 = visited + 4 + 4 * Visited.Count;

        foreach (var value in new[]
                 {
                     (uint)formId, (uint)unknown3, (uint)gd1, (uint)gd2, (uint)cf, (uint)gd3,
                     1u, 1u, 1u, (uint)ChangeForms.Count
                 })
            out_.AddRange(BitConverter.GetBytes(value));
        for (var i = 0; i < 15; i++) out_.AddRange(BitConverter.GetBytes((uint)(0xF0 + i)));

        out_.AddRange(Gd1);
        out_.AddRange(Gd2);
        out_.AddRange(changeForms);
        out_.AddRange(Gd3);
        out_.AddRange(BitConverter.GetBytes((uint)FormIds.Count));
        foreach (var id in FormIds) out_.AddRange(BitConverter.GetBytes(id));
        out_.AddRange(BitConverter.GetBytes((uint)Visited.Count));
        foreach (var id in Visited) out_.AddRange(BitConverter.GetBytes(id));
        out_.AddRange(Unknown3);

        return out_.ToArray();
    }

    public string WriteTo(string folder, string fileName)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, Build());
        return path;
    }

    private static byte[] WStr(string s)
    {
        var body = Win1252.GetBytes(s);
        var buffer = new byte[2 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)body.Length);
        body.CopyTo(buffer, 2);
        return buffer;
    }

    /// <summary>
    /// A save that leans on two plugins, one of which the tests treat as gone: three plugins,
    /// form IDs from each, and a ChangeForm pointing at every form ID slot.
    /// </summary>
    public static SaveBuilder WithMixedContent()
    {
        var builder = new SaveBuilder
        {
            Plugins = ["Fallout4.esm", "GhostMod.esp", "KeeperMod.esp"],
            LightPlugins = ["GhostLight.esl", "KeeperLight.esl"],
            FormIds =
            [
                RegularForm(0, 0x0F99EC),   // slot 0 - base game
                RegularForm(1, 0x001111),   // slot 1 - GhostMod (missing in tests)
                RegularForm(2, 0x002222),   // slot 2 - KeeperMod
                LightForm(0, 0x111),        // slot 3 - GhostLight (missing in tests)
                LightForm(1, 0x222),        // slot 4 - KeeperLight
                CreatedForm(0x004321)       // slot 5 - created during play
            ]
        };

        for (var slot = 0; slot < builder.FormIds.Count; slot++)
            builder.ChangeForms.Add(ChangeForm(0, slot, [.. Enumerable.Repeat((byte)slot, 8 + slot)], slot % 3));
        builder.ChangeForms.Add(ChangeForm(1, 0x0F99EC, [1, 2, 3], 1));   // base game reference
        builder.ChangeForms.Add(ChangeForm(2, 0x000123, [4, 5], 0));      // created form reference
        return builder;
    }
}
