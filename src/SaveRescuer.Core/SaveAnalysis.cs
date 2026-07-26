namespace SaveRescuer.Core;

/// <summary>How likely a rescue is to hold up in game, judged by the save's size.</summary>
public enum RescueOutlook
{
    /// <summary>Nothing is missing; the save does not need surgery.</summary>
    NothingToDo,
    Good,
    /// <summary>Beyond what has been measured either way.</summary>
    Uncertain,
    /// <summary>Heavy enough that the game has been seen to reject the result.</summary>
    Risky
}

/// <summary>
/// What a save depends on and what removing the missing part of it would cost.
/// </summary>
public sealed class SaveAnalysis
{
    /// <summary>Rescues held up in game at this ChangeForm count and below.</summary>
    public const int MeasuredGoodCeiling = 100_000;
    /// <summary>Above this the one observed failure sits (196,630 ChangeForms).</summary>
    public const int MeasuredRiskFloor = 150_000;

    public required SaveFile Save { get; init; }
    public required PluginInventory Plugins { get; init; }

    /// <summary>Form ID slots owned by plugins that are gone.</summary>
    public required HashSet<int> DeadFormIdSlots { get; init; }
    /// <summary>ChangeForms that reference one of those slots.</summary>
    public required int DeadChangeForms { get; init; }
    /// <summary>ChangeForms the walker could actually read; short of the count means damage.</summary>
    public required long WalkedChangeForms { get; init; }
    public required List<string> LayoutIssues { get; init; }

    public int FormIdCount => Save.FormIds.Count;
    public long ChangeFormBytes => Save.ChangeForms.Length;
    public bool IsIntact => LayoutIssues.Count == 0 && WalkedChangeForms == Save.ChangeFormCount;

    public RescueOutlook Outlook =>
        Plugins.MissingCount == 0 ? RescueOutlook.NothingToDo
        : Save.ChangeFormCount < MeasuredGoodCeiling ? RescueOutlook.Good
        : Save.ChangeFormCount < MeasuredRiskFloor ? RescueOutlook.Uncertain
        : RescueOutlook.Risky;

    /// <summary>A plain reading of the outlook, with the number it rests on.</summary>
    public string OutlookNote => Outlook switch
    {
        RescueOutlook.NothingToDo =>
            "Every plugin this save needs is installed, so there is nothing to remove.",
        RescueOutlook.Good =>
            $"{Save.ChangeFormCount:N0} change forms. Saves of this weight have come back reliably.",
        RescueOutlook.Uncertain =>
            $"{Save.ChangeFormCount:N0} change forms - heavier than any save that has been " +
            "confirmed working, lighter than the one that failed. Worth trying.",
        _ =>
            $"{Save.ChangeFormCount:N0} change forms over {ChangeFormBytes / 1024.0 / 1024.0:N0} MB. " +
            "A save this heavy has been seen to load and then reject the next save written over it."
    };

    public static SaveAnalysis Run(SaveFile save, PluginInventory plugins)
    {
        var dead = new HashSet<int>();
        for (var slot = 0; slot < save.FormIds.Count; slot++)
        {
            var owner = ChangeForms.OwnerOf(save.FormIds[slot], save.Plugins.Count, save.LightPlugins.Count);
            var isDead = owner.Kind switch
            {
                FormOwnerKind.Regular => plugins.MissingRegular.Contains(owner.Index),
                FormOwnerKind.Light => plugins.MissingLight.Contains(owner.Index),
                FormOwnerKind.OutOfRange => true,
                _ => false
            };
            if (isDead) dead.Add(slot);
        }

        long walked = 0;
        var deadChangeForms = 0;
        foreach (var entry in ChangeForms.Walk(save.ChangeForms, save.ChangeFormCount))
        {
            walked++;
            if (entry.PointsAtFormIdArray && dead.Contains(entry.RefValue)) deadChangeForms++;
        }

        return new SaveAnalysis
        {
            Save = save,
            Plugins = plugins,
            DeadFormIdSlots = dead,
            DeadChangeForms = deadChangeForms,
            WalkedChangeForms = walked,
            LayoutIssues = save.LayoutIssues()
        };
    }

    /// <summary>The missing plugins that account for the most dead forms.</summary>
    public List<(PluginEntry Plugin, int FormCount)> HeaviestMissing(int take = 5)
    {
        var counts = new Dictionary<(bool Light, int Index), int>();
        foreach (var slot in DeadFormIdSlots)
        {
            var owner = ChangeForms.OwnerOf(Save.FormIds[slot], Save.Plugins.Count, Save.LightPlugins.Count);
            if (owner.Kind is not (FormOwnerKind.Regular or FormOwnerKind.Light)) continue;
            var key = (owner.Kind == FormOwnerKind.Light, owner.Index);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts.OrderByDescending(kv => kv.Value)
                     .Select(kv => (Plugin: Plugins.Entries.FirstOrDefault(
                                        e => e.IsLight == kv.Key.Light && e.Index == kv.Key.Index),
                                    FormCount: kv.Value))
                     .Where(x => x.Plugin is not null)
                     .Take(take)
                     .Select(x => (x.Plugin!, x.FormCount))
                     .ToList();
    }
}
