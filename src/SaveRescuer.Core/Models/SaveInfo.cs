namespace SaveRescuer.Core.Models;

/// <summary>Parsed contents of a Fallout 4 save file (.fos).</summary>
public sealed class SaveInfo
{
    public string FilePath { get; init; } = "";
    public string FileName => Path.GetFileName(FilePath);

    public uint SaveVersion { get; init; }
    public uint SaveNumber { get; init; }
    public string CharacterName { get; init; } = "";
    public uint CharacterLevel { get; init; }
    public string Location { get; init; } = "";
    public string PlayTime { get; init; } = "";
    public string RaceEditorId { get; init; } = "";
    public ushort Sex { get; init; }
    public string GameVersion { get; init; } = "";
    public byte FormVersion { get; init; }
    public DateTime SavedAt { get; init; }

    /// <summary>Regular plugins the save expects, in save order.</summary>
    public List<string> Plugins { get; init; } = [];

    /// <summary>Light (ESL/Creation) plugins the save expects. Present on next-gen saves.</summary>
    public List<string> LightPlugins { get; init; } = [];

    /// <summary>Embedded screenshot, if the save carries one.</summary>
    public int ScreenshotWidth { get; init; }
    public int ScreenshotHeight { get; init; }
    public byte[]? ScreenshotRgba { get; init; }

    public int TotalPluginCount => Plugins.Count + LightPlugins.Count;

    public IEnumerable<string> AllPlugins => Plugins.Concat(LightPlugins);
}
