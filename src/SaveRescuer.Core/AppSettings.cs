using System.Text.Json;

namespace SaveRescuer.Core;

/// <summary>What the tool remembers between runs. Missing or unreadable settings fall back to detection.</summary>
public sealed class AppSettings
{
    public string? DataFolder { get; set; }
    public string? SavesFolder { get; set; }
    public string? VaultFolder { get; set; }
    public string Prefix { get; set; } = "[RESCUED] ";
    /// <summary>Strip every plugin outside the base game rather than only the ones that are gone.</summary>
    public bool VanillaOnly { get; set; }
    public bool BackupBeforeRescue { get; set; } = true;
    public bool ReplaceExistingTag { get; set; } = true;
    /// <summary>Manual slot numbers start here, clear of the ones the game uses.</summary>
    public int SlotStart { get; set; } = 900;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FO4SaveRescuer", "settings.json");

    public SurgeryOptions ToSurgeryOptions() => new()
    {
        Prefix = Prefix,
        ReplaceExistingTag = ReplaceExistingTag,
        VanillaOnly = VanillaOnly
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? Detected();
        }
        catch (Exception ex) when (ex is JsonException or IOException) { /* fall through to detection */ }
        return Detected();
    }

    public static AppSettings Detected() => new()
    {
        DataFolder = GameEnvironment.FindDataFolder(),
        SavesFolder = GameEnvironment.FindSavesFolder()
    };

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
