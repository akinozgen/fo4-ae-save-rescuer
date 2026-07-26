using Microsoft.Win32;

namespace SaveRescuer.Core;

/// <summary>
/// Locations the tool needs: the game's Data folder, the active plugins.txt and the save folder.
/// Everything is auto-detected but each path can be overridden by the caller.
/// </summary>
public sealed class GameEnvironment
{
    public string? DataFolder { get; set; }
    public string? PluginsTxt { get; set; }
    public string? SavesFolder { get; set; }

    /// <summary>Extra plugins.txt candidates (e.g. Mod Organizer 2 profiles) for the user to pick from.</summary>
    public List<string> PluginsTxtCandidates { get; } = [];

    public bool IsComplete => Directory.Exists(DataFolder) && File.Exists(PluginsTxt);

    public static GameEnvironment Detect()
    {
        var env = new GameEnvironment
        {
            DataFolder = FindDataFolder(),
            SavesFolder = FindSavesFolder()
        };

        var vanilla = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fallout4", "plugins.txt");
        if (File.Exists(vanilla))
            env.PluginsTxtCandidates.Add(vanilla);

        env.PluginsTxtCandidates.AddRange(FindMo2ProfilePluginLists());
        env.PluginsTxt = env.PluginsTxtCandidates.FirstOrDefault();
        return env;
    }

    /// <summary>Game folder from the registry, falling back to a scan of Steam library folders.</summary>
    public static string? FindDataFolder()
    {
        foreach (var root in new[]
                 {
                     @"SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4",
                     @"SOFTWARE\Bethesda Softworks\Fallout4"
                 })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key?.GetValue("installed path") is string p && Directory.Exists(p))
                {
                    var data = Path.Combine(p, "Data");
                    if (Directory.Exists(data)) return data;
                }
            }
            catch { /* registry unavailable - fall through to Steam scan */ }
        }

        foreach (var lib in SteamLibraryFolders())
        {
            var data = Path.Combine(lib, "steamapps", "common", "Fallout 4", "Data");
            if (Directory.Exists(data)) return data;
        }
        return null;
    }

    private static IEnumerable<string> SteamLibraryFolders()
    {
        string? steam = null;
        foreach (var root in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key?.GetValue("InstallPath") is string p) { steam = p; break; }
            }
            catch { /* ignore */ }
        }
        if (steam is null) yield break;
        yield return steam;

        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        // The VDF is a simple key/value tree; only the "path" values are of interest here.
        foreach (var line in File.ReadLines(vdf))
        {
            var t = line.Trim();
            if (!t.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = t.Split('"', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                var path = parts[^1].Replace(@"\\", @"\").Trim();
                if (Directory.Exists(path)) yield return path;
            }
        }
    }

    /// <summary>Documents\My Games\Fallout4\Saves, tolerating OneDrive-redirected Documents.</summary>
    public static string? FindSavesFolder()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         "My Games", "Fallout4", "Saves")
        };

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(profile, "Documents", "My Games", "Fallout4", "Saves"));
        candidates.Add(Path.Combine(profile, "OneDrive", "Documents", "My Games", "Fallout4", "Saves"));
        candidates.Add(Path.Combine(profile, "OneDrive", "Belgeler", "My Games", "Fallout4", "Saves"));

        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>Mod Organizer 2 keeps a plugins.txt per profile; list any that exist.</summary>
    public static IEnumerable<string> FindMo2ProfilePluginLists()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModOrganizer"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModOrganizer")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var instance in Directory.EnumerateDirectories(root))
            {
                var profiles = Path.Combine(instance, "profiles");
                if (!Directory.Exists(profiles)) continue;
                foreach (var profile in Directory.EnumerateDirectories(profiles))
                {
                    var file = Path.Combine(profile, "plugins.txt");
                    if (File.Exists(file)) yield return file;
                }
            }
        }
    }
}
