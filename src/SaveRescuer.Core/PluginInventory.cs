namespace SaveRescuer.Core;

public enum PluginState
{
    /// <summary>Base game or an official DLC: always present, never removed.</summary>
    BaseGame,
    /// <summary>The file is in the Data folder.</summary>
    Installed,
    /// <summary>The file is gone. Everything this plugin put in the save has to come out.</summary>
    Missing
}

public sealed record PluginEntry(int Index, string Name, bool IsLight, PluginState State)
{
    /// <summary>Index as the save's form IDs encode it, for the plugin column in the interface.</summary>
    public string IndexLabel => IsLight ? $"FE:{Index:X3}" : $"{Index:X2}";
}

/// <summary>
/// Decides which plugins a save depends on and which of those are no longer installed.
///
/// The only criterion is whether the file exists in the Data folder. Fallout4.ccc lists every
/// piece of Creation content that exists rather than what this copy owns, so a name appearing
/// there says nothing about availability.
/// </summary>
public sealed class PluginInventory
{
    public static readonly HashSet<string> BaseGame = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm",
        "DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm",
        "DLCUltraHighResolution.esm"
    };

    public IReadOnlyList<PluginEntry> Entries { get; }
    public IReadOnlySet<int> MissingRegular { get; }
    public IReadOnlySet<int> MissingLight { get; }

    public int MissingCount => MissingRegular.Count + MissingLight.Count;
    public IEnumerable<PluginEntry> Missing => Entries.Where(e => e.State == PluginState.Missing);

    private PluginInventory(List<PluginEntry> entries)
    {
        Entries = entries;
        MissingRegular = entries.Where(e => !e.IsLight && e.State == PluginState.Missing)
                                .Select(e => e.Index).ToHashSet();
        MissingLight = entries.Where(e => e.IsLight && e.State == PluginState.Missing)
                              .Select(e => e.Index).ToHashSet();
    }

    /// <param name="dataFolder">The game's Data folder; when it cannot be read nothing counts as installed.</param>
    /// <param name="vanillaOnly">
    /// Treat every plugin outside the base game as missing. Produces a save that depends on
    /// nothing but the game itself - the safest output, and the only option when the mods that
    /// built the save are not available any more.
    /// </param>
    public static PluginInventory Build(SaveFile save, string? dataFolder, bool vanillaOnly = false) =>
        Build(save.Plugins, save.LightPlugins, ReadInstalled(dataFolder), vanillaOnly);

    public static PluginInventory Build(SaveSummary summary, IReadOnlySet<string> installed, bool vanillaOnly = false) =>
        Build(summary.Plugins, summary.LightPlugins, installed, vanillaOnly);

    /// <summary>
    /// Takes a pre-read set of installed plugin names, so a folder full of saves can be judged
    /// without walking the Data folder once per save.
    /// </summary>
    public static PluginInventory Build(
        IReadOnlyList<string> plugins, IReadOnlyList<string> lightPlugins,
        IReadOnlySet<string> installed, bool vanillaOnly = false)
    {
        PluginState StateOf(string name)
        {
            if (BaseGame.Contains(name)) return PluginState.BaseGame;
            if (vanillaOnly) return PluginState.Missing;
            return installed.Contains(name) ? PluginState.Installed : PluginState.Missing;
        }

        var entries = new List<PluginEntry>(plugins.Count + lightPlugins.Count);
        for (var i = 0; i < plugins.Count; i++)
            entries.Add(new PluginEntry(i, plugins[i], false, StateOf(plugins[i])));
        for (var i = 0; i < lightPlugins.Count; i++)
            entries.Add(new PluginEntry(i, lightPlugins[i], true, StateOf(lightPlugins[i])));

        return new PluginInventory(entries);
    }

    /// <summary>The plugin file names present in the Data folder.</summary>
    public static IReadOnlySet<string> ReadInstalled(string? dataFolder)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (dataFolder is null || !Directory.Exists(dataFolder)) return installed;
        foreach (var file in Directory.EnumerateFiles(dataFolder))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
                installed.Add(name);
        }
        return installed;
    }
}
