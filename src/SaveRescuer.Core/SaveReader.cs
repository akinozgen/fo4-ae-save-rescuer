using System.Text;
using SaveRescuer.Core.Models;

namespace SaveRescuer.Core;

/// <summary>
/// Reads Fallout 4 save files (.fos). Only the header and plugin tables are parsed;
/// the rest of the file (change forms, global data) is never touched or rewritten.
/// </summary>
public static class SaveReader
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FO4_SAVEGAME");

    public static bool LooksLikeSave(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[Magic.Length];
            return fs.Read(buf, 0, buf.Length) == buf.Length && buf.SequenceEqual(Magic);
        }
        catch { return false; }
    }

    public static SaveInfo Read(string path, bool includeScreenshot = true)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs, Encoding.Latin1);

        var magic = br.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Not a Fallout 4 save file (missing FO4_SAVEGAME signature).");

        uint headerSize = br.ReadUInt32();
        long headerStart = fs.Position;

        uint saveVersion = br.ReadUInt32();
        uint saveNumber = br.ReadUInt32();
        string name = ReadWString(br);
        uint level = br.ReadUInt32();
        string location = ReadWString(br);
        string playTime = ReadWString(br);
        string race = ReadWString(br);
        ushort sex = br.ReadUInt16();
        br.ReadSingle();                       // current experience
        br.ReadSingle();                       // experience required for next level
        long fileTime = br.ReadInt64();
        int shotWidth = br.ReadInt32();
        int shotHeight = br.ReadInt32();

        // Jump to the exact end of the header: later game versions append fields here.
        fs.Position = headerStart + headerSize;

        byte[]? shot = null;
        long shotBytes = (long)shotWidth * shotHeight * 4;
        if (includeScreenshot && shotWidth > 0 && shotHeight > 0 && shotBytes < 64 * 1024 * 1024)
            shot = br.ReadBytes((int)shotBytes);
        else
            fs.Position += shotBytes;

        byte formVersion = br.ReadByte();
        string gameVersion = ReadWString(br);

        uint pluginInfoSize = br.ReadUInt32();
        long pluginInfoStart = fs.Position;

        var plugins = new List<string>();
        byte pluginCount = br.ReadByte();
        for (int i = 0; i < pluginCount; i++)
            plugins.Add(ReadWString(br));

        // Next-gen saves store light (ESL/Creation) plugins in the remainder of the block.
        var light = new List<string>();
        long remaining = pluginInfoStart + pluginInfoSize - fs.Position;
        if (remaining > 2)
        {
            ushort lightCount = br.ReadUInt16();
            for (int i = 0; i < lightCount && fs.Position < pluginInfoStart + pluginInfoSize; i++)
                light.Add(ReadWString(br));
        }

        return new SaveInfo
        {
            FilePath = path,
            SaveVersion = saveVersion,
            SaveNumber = saveNumber,
            CharacterName = name,
            CharacterLevel = level,
            Location = location,
            PlayTime = playTime,
            RaceEditorId = race,
            Sex = sex,
            GameVersion = gameVersion,
            FormVersion = formVersion,
            SavedAt = SafeFileTime(fileTime),
            Plugins = plugins,
            LightPlugins = light,
            ScreenshotWidth = shotWidth,
            ScreenshotHeight = shotHeight,
            ScreenshotRgba = shot
        };
    }

    private static string ReadWString(BinaryReader br)
    {
        ushort len = br.ReadUInt16();
        return len == 0 ? "" : Encoding.Latin1.GetString(br.ReadBytes(len)).TrimEnd('\0');
    }

    private static DateTime SafeFileTime(long fileTime)
    {
        try { return DateTime.FromFileTime(fileTime); }
        catch { return DateTime.MinValue; }
    }
}
