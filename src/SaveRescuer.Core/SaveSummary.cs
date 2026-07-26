using System.Buffers.Binary;

namespace SaveRescuer.Core;

/// <summary>
/// Everything the save list needs, read without pulling the whole file into memory. A rescue
/// candidate can be tens of megabytes, and the folder can hold hundreds of them.
/// </summary>
public sealed class SaveSummary
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required long FileSize { get; init; }
    public required DateTime WrittenAt { get; init; }

    public uint SaveNumber { get; init; }
    public string PlayerName { get; init; } = "";
    public uint Level { get; init; }
    /// <summary>The name the in-game load menu shows, which is where the rescue prefix lands.</summary>
    public string Location { get; init; } = "";
    public string PlayTime { get; init; } = "";
    public string Race { get; init; } = "";
    public byte FormVersion { get; init; }
    public string GameVersion { get; init; } = "";

    public List<string> Plugins { get; init; } = [];
    public List<string> LightPlugins { get; init; } = [];
    public uint ChangeFormCount { get; init; }
    public int FormIdCount { get; init; }

    public uint ShotWidth { get; init; }
    public uint ShotHeight { get; init; }
    private long ScreenshotOffset { get; init; }

    /// <summary>Problem found while reading; the entry is still listed so the user sees it.</summary>
    public string? Error { get; init; }

    public bool IsReadable => Error is null;
    public double MegaBytes => FileSize / 1024.0 / 1024.0;
    public int PluginCount => Plugins.Count + LightPlugins.Count;
    /// <summary>Autosave and quicksave slots get overwritten by the game; manual slots do not.</summary>
    public bool IsRotatingSlot =>
        FileName.StartsWith("Autosave", StringComparison.OrdinalIgnoreCase) ||
        FileName.StartsWith("Quicksave", StringComparison.OrdinalIgnoreCase);

    public static SaveSummary Read(string path)
    {
        var info = new FileInfo(path);
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ReadFrom(stream, path, info);
        }
        catch (Exception ex)
        {
            return new SaveSummary
            {
                Path = path,
                FileName = info.Name,
                FileSize = info.Exists ? info.Length : 0,
                WrittenAt = info.Exists ? info.LastWriteTime : DateTime.MinValue,
                Error = ex is SaveFormatException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private static SaveSummary ReadFrom(Stream stream, string path, FileInfo info)
    {
        if (Win1252.GetString(Take(stream, 12)) != SaveFile.Magic)
            throw new SaveFormatException("Not a Fallout 4 save: the FO4_SAVEGAME marker is missing.");

        var headerSize = ReadU32(stream);
        var header = Take(stream, (int)headerSize);

        var q = 0;
        _ = HU32(header, ref q);                 // save version
        var saveNumber = HU32(header, ref q);
        var playerName = HWStr(header, ref q);
        var level = HU32(header, ref q);
        var location = HWStr(header, ref q);
        var playTime = HWStr(header, ref q);
        var race = HWStr(header, ref q);
        q += 2 + 4 + 4 + 8;                      // sex, experience, experience to level, filetime
        var shotWidth = HU32(header, ref q);
        var shotHeight = HU32(header, ref q);

        var screenshotOffset = stream.Position;
        stream.Seek(checked((long)shotWidth * shotHeight * 4), SeekOrigin.Current);

        var formVersion = (byte)stream.ReadByte();
        var gameVersion = ReadWStr(stream);

        var pluginInfoSize = ReadU32(stream);
        var blockStart = stream.Position;
        var count = stream.ReadByte();
        var plugins = new List<string>(count);
        for (var i = 0; i < count; i++) plugins.Add(ReadWStr(stream));

        var lightPlugins = new List<string>();
        if (blockStart + pluginInfoSize - stream.Position >= 2)
        {
            var lightCount = ReadU16(stream);
            for (var i = 0; i < lightCount; i++) lightPlugins.Add(ReadWStr(stream));
        }

        stream.Seek(blockStart + pluginInfoSize, SeekOrigin.Begin);
        var formIdOffset = ReadU32(stream);
        stream.Seek(4 * 4, SeekOrigin.Current);           // unknown3, GD1, GD2, ChangeForms offsets
        stream.Seek(4 + 4 * 3, SeekOrigin.Current);       // GD3 offset, GD1-3 counts
        var changeFormCount = ReadU32(stream);

        stream.Seek(formIdOffset, SeekOrigin.Begin);
        var formIdCount = (int)ReadU32(stream);

        return new SaveSummary
        {
            Path = path,
            FileName = info.Name,
            FileSize = info.Length,
            WrittenAt = info.LastWriteTime,
            SaveNumber = saveNumber,
            PlayerName = playerName,
            Level = level,
            Location = location,
            PlayTime = playTime,
            Race = race,
            FormVersion = formVersion,
            GameVersion = gameVersion,
            Plugins = plugins,
            LightPlugins = lightPlugins,
            ChangeFormCount = changeFormCount,
            FormIdCount = formIdCount,
            ShotWidth = shotWidth,
            ShotHeight = shotHeight,
            ScreenshotOffset = screenshotOffset
        };
    }

    /// <summary>The in-game screenshot as raw RGBA rows, or null when it cannot be read.</summary>
    public byte[]? ReadScreenshot()
    {
        if (ShotWidth == 0 || ShotHeight == 0) return null;
        try
        {
            using var stream = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(ScreenshotOffset, SeekOrigin.Begin);
            return Take(stream, checked((int)(ShotWidth * ShotHeight * 4)));
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Take(Stream stream, int count)
    {
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static uint ReadU32(Stream stream) => BinaryPrimitives.ReadUInt32LittleEndian(Take(stream, 4));
    private static ushort ReadU16(Stream stream) => BinaryPrimitives.ReadUInt16LittleEndian(Take(stream, 2));
    private static string ReadWStr(Stream stream) => Win1252.GetString(Take(stream, ReadU16(stream)));

    private static uint HU32(byte[] header, ref int q)
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(q, 4));
        q += 4;
        return v;
    }

    private static string HWStr(byte[] header, ref int q)
    {
        var n = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(q, 2));
        var s = Win1252.GetString(header.AsSpan(q + 2, n));
        q += 2 + n;
        return s;
    }
}
