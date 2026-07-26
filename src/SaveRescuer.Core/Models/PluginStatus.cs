namespace SaveRescuer.Core.Models;

public enum PluginState
{
    /// <summary>Present in Data and enabled in the load order.</summary>
    Ok,
    /// <summary>File exists in Data but is not enabled in the load order.</summary>
    NotEnabled,
    /// <summary>No file in Data - the save's warning dialog is caused by these.</summary>
    Missing,
    /// <summary>Present because this tool created an empty placeholder for it.</summary>
    Stub,
    /// <summary>Base game or DLC master; always loaded, never listed in the load order.</summary>
    BaseGame
}

public sealed record PluginStatus(string Name, bool IsLight, PluginState State)
{
    public bool NeedsAction => State is PluginState.Missing or PluginState.NotEnabled;
}

/// <summary>What a save needs versus what the current setup provides.</summary>
public sealed class SaveDiagnosis
{
    public required Models.SaveInfo Save { get; init; }
    public List<PluginStatus> Plugins { get; init; } = [];

    public IEnumerable<PluginStatus> Missing => Plugins.Where(p => p.State == PluginState.Missing);
    public IEnumerable<PluginStatus> NotEnabled => Plugins.Where(p => p.State == PluginState.NotEnabled);
    public IEnumerable<PluginStatus> Stubs => Plugins.Where(p => p.State == PluginState.Stub);

    public int MissingCount => Missing.Count();
    public int NotEnabledCount => NotEnabled.Count();
    public bool CanLoadCleanly => MissingCount == 0 && NotEnabledCount == 0;
}
