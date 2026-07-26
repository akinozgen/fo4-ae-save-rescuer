using System.Text.Json;
using SaveRescuer.Core.Models;

namespace SaveRescuer.Core;

/// <summary>Outcome of a rescue or revert operation, ready to show in a log pane.</summary>
public sealed class RescueResult
{
    public List<string> CreatedStubs { get; } = [];
    public List<string> EnabledPlugins { get; } = [];
    public List<string> RemovedStubs { get; } = [];
    public string? LoadOrderBackup { get; set; }
    public List<string> Messages { get; } = [];
    public bool AnyChange => CreatedStubs.Count > 0 || EnabledPlugins.Count > 0 || RemovedStubs.Count > 0;
}

/// <summary>Record of what a rescue created, so it can be undone later.</summary>
public sealed class RescueManifest
{
    public string SaveFile { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string DataFolder { get; set; } = "";
    public string PluginsTxt { get; set; } = "";
    public string? LoadOrderBackup { get; set; }
    public List<string> Stubs { get; set; } = [];
    public List<string> Enabled { get; set; } = [];
}

/// <summary>
/// Diagnoses a save against the current setup and, on request, creates placeholder plugins
/// plus load-order entries so the save can be opened. Every change is reversible.
/// </summary>
public sealed class RescueService(GameEnvironment env)
{
    private static readonly HashSet<string> BaseGame = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm",
        "DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm",
        "DLCUltraHighResolution.esm"
    };

    public GameEnvironment Environment { get; } = env;

    private string ManifestPath => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "FO4SaveRescuer", "last-rescue.json");

    /// <summary>
    /// True while Fallout 4 is running. The game keeps its plugin files open, so writing or
    /// deleting placeholders would fail; the caller should ask the user to close the game first.
    /// </summary>
    public static bool IsGameRunning() =>
        System.Diagnostics.Process.GetProcessesByName("Fallout4").Length > 0;

    public SaveDiagnosis Diagnose(SaveInfo save)
    {
        var data = Environment.DataFolder ?? throw new InvalidOperationException("Data folder is not set.");
        var order = LoadOrderFile.Load(Environment.PluginsTxt ?? "");

        var diagnosis = new SaveDiagnosis { Save = save };
        foreach (var (name, isLight) in save.Plugins.Select(p => (p, false))
                                            .Concat(save.LightPlugins.Select(p => (p, true))))
        {
            diagnosis.Plugins.Add(new PluginStatus(name, isLight, Classify(name, data, order)));
        }
        return diagnosis;
    }

    private static PluginState Classify(string name, string dataFolder, LoadOrderFile order)
    {
        if (BaseGame.Contains(name)) return PluginState.BaseGame;

        var path = Path.Combine(dataFolder, name);
        if (!File.Exists(path)) return PluginState.Missing;
        if (StubPlugin.IsStub(path)) return PluginState.Stub;
        return order.IsEnabled(name) ? PluginState.Ok : PluginState.NotEnabled;
    }

    /// <summary>Creates placeholders for missing plugins and enables everything the save expects.</summary>
    public RescueResult Rescue(SaveInfo save, bool dryRun = false)
    {
        var result = new RescueResult();
        var data = Environment.DataFolder ?? throw new InvalidOperationException("Data folder is not set.");
        var pluginsTxt = Environment.PluginsTxt ?? throw new InvalidOperationException("plugins.txt is not set.");

        var diagnosis = Diagnose(save);
        var toStub = diagnosis.Missing.ToList();
        var toEnable = diagnosis.Plugins
            .Where(p => p.State is PluginState.Missing or PluginState.NotEnabled or PluginState.Stub)
            .ToList();

        if (toStub.Count == 0 && diagnosis.NotEnabledCount == 0)
        {
            result.Messages.Add("This save already has everything it needs - no changes required.");
            return result;
        }

        if (dryRun)
        {
            foreach (var p in toStub) result.CreatedStubs.Add(p.Name);
            foreach (var p in toEnable.Where(x => x.State != PluginState.Stub)) result.EnabledPlugins.Add(p.Name);
            result.Messages.Add("Preview only - nothing was written.");
            return result;
        }

        if (IsGameRunning())
            throw new InvalidOperationException(
                "Fallout 4 is running. Close the game first - it keeps its plugin files locked.");

        var order = LoadOrderFile.Load(pluginsTxt);
        result.LoadOrderBackup = order.Backup();

        foreach (var p in toStub)
        {
            var target = Path.Combine(data, p.Name);
            if (File.Exists(target)) continue;              // never overwrite real content
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            StubPlugin.Write(target, p.IsLight);
            result.CreatedStubs.Add(p.Name);
        }

        foreach (var p in toEnable)
        {
            if (order.Enable(p.Name)) result.EnabledPlugins.Add(p.Name);
        }
        order.Save();

        SaveManifest(new RescueManifest
        {
            SaveFile = save.FilePath,
            CharacterName = save.CharacterName,
            CreatedAt = DateTime.Now,
            DataFolder = data,
            PluginsTxt = pluginsTxt,
            LoadOrderBackup = result.LoadOrderBackup,
            Stubs = result.CreatedStubs.ToList(),
            Enabled = result.EnabledPlugins.ToList()
        });

        result.Messages.Add($"{result.CreatedStubs.Count} placeholder(s) created, " +
                            $"{result.EnabledPlugins.Count} load-order entr(ies) enabled.");
        return result;
    }

    /// <summary>Removes placeholders created earlier and restores the load order.</summary>
    public RescueResult Revert()
    {
        var result = new RescueResult();
        var manifest = LoadManifest();
        if (manifest is null)
        {
            result.Messages.Add("No previous rescue was found on this machine.");
            return result;
        }

        if (IsGameRunning())
            throw new InvalidOperationException(
                "Fallout 4 is running. Close the game first - it keeps its plugin files locked.");

        foreach (var name in manifest.Stubs)
        {
            var path = Path.Combine(manifest.DataFolder, name);
            if (StubPlugin.IsStub(path))          // only ever delete our own placeholders
            {
                File.Delete(path);
                result.RemovedStubs.Add(name);
            }
            else if (File.Exists(path))
            {
                result.Messages.Add($"Left alone (not a placeholder any more): {name}");
            }
        }

        if (manifest.LoadOrderBackup is not null && File.Exists(manifest.LoadOrderBackup))
        {
            File.Copy(manifest.LoadOrderBackup, manifest.PluginsTxt, overwrite: true);
            result.Messages.Add("Load order restored from backup.");
        }
        else
        {
            var order = LoadOrderFile.Load(manifest.PluginsTxt);
            foreach (var name in manifest.Stubs) order.Remove(name);
            order.Save();
            result.Messages.Add("Backup missing; placeholder entries were removed from the load order instead.");
        }

        File.Delete(ManifestPath);
        return result;
    }

    public RescueManifest? LoadManifest()
    {
        try
        {
            return File.Exists(ManifestPath)
                ? JsonSerializer.Deserialize<RescueManifest>(File.ReadAllText(ManifestPath))
                : null;
        }
        catch { return null; }
    }

    private void SaveManifest(RescueManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        File.WriteAllText(ManifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Every save in a folder, newest first.</summary>
    public static IEnumerable<SaveInfo> EnumerateSaves(string folder)
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in new DirectoryInfo(folder).GetFiles("*.fos")
                     .OrderByDescending(f => f.LastWriteTime))
        {
            SaveInfo? info = null;
            try { info = SaveReader.Read(file.FullName, includeScreenshot: false); }
            catch { /* skip unreadable saves */ }
            if (info is not null) yield return info;
        }
    }
}
