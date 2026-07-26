using System.Text.RegularExpressions;

namespace SaveRescuer.Core;

public sealed record SurgeryOptions
{
    /// <summary>Prepended to the in-game name so a rescued save is recognisable in the load menu.</summary>
    public string Prefix { get; init; } = "[RESCUED] ";
    /// <summary>Drop an existing bracketed tag instead of stacking a second one in front of it.</summary>
    public bool ReplaceExistingTag { get; init; } = true;
    /// <summary>Remove every plugin outside the base game, not just the ones that are gone.</summary>
    public bool VanillaOnly { get; init; }
}

public sealed record Check(string Name, bool Passed, string Detail);

public sealed record SurgeryOutcome
{
    public required byte[] Bytes { get; init; }
    public required string InGameName { get; init; }
    public required int RemovedRegular { get; init; }
    public required int RemovedLight { get; init; }
    public required int ZeroedFormIds { get; init; }
    public required int RemappedFormIds { get; init; }
    public required int DroppedChangeForms { get; init; }
    public required long OriginalBytes { get; init; }
    public required List<Check> Checks { get; init; }

    public long NewBytes => Bytes.LongLength;
    public long BytesSaved => OriginalBytes - NewBytes;
    public bool Passed => Checks.All(c => c.Passed);
}

/// <summary>
/// Takes the missing content out of a save so the game stops looking for it.
///
/// Three things have to happen together, and the order matters:
///   - the plugin lists lose the missing names, which shifts every later plugin index;
///   - each form ID is rewritten to its new index, or zeroed when its plugin is gone. Slot
///     positions never move: other sections of the save index into this array by position, so
///     compacting it would silently repoint them;
///   - ChangeForms whose reference is one of those zeroed slots are dropped.
/// </summary>
public static class SaveSurgeon
{
    private static readonly Regex LeadingTag = new(@"^\s*\[[^\]]{1,24}\]\s*", RegexOptions.Compiled);

    /// <summary>
    /// Performs the operation on <paramref name="save"/> in memory and returns the new file.
    /// The instance is modified, so load a fresh copy if the original is needed afterwards.
    /// </summary>
    public static SurgeryOutcome Operate(SaveFile save, PluginInventory plugins, SurgeryOptions options)
    {
        var originalBytes = save.Raw.LongLength;
        var missingRegular = plugins.MissingRegular;
        var missingLight = plugins.MissingLight;

        // Old index -> new index for everything that survives.
        var regularMap = SurvivorMap(save.Plugins.Count, missingRegular);
        var lightMap = SurvivorMap(save.LightPlugins.Count, missingLight);

        var deadSlots = new HashSet<int>();
        var remapped = 0;
        var newFormIds = new List<uint>(save.FormIds.Count);
        for (var slot = 0; slot < save.FormIds.Count; slot++)
        {
            var formId = save.FormIds[slot];
            var hi = formId >> 24;

            if (hi == 0xFF)                       // created during play, belongs to no plugin
            {
                newFormIds.Add(formId);
                continue;
            }

            if (hi == 0xFE)                       // light plugin form
            {
                var sub = (int)((formId >> 12) & 0xFFF);
                if (!lightMap.TryGetValue(sub, out var newSub))
                {
                    deadSlots.Add(slot);
                    newFormIds.Add(0);
                    continue;
                }
                if (newSub != sub) remapped++;
                newFormIds.Add((0xFEu << 24) | ((uint)newSub << 12) | (formId & 0xFFF));
                continue;
            }

            if (!regularMap.TryGetValue((int)hi, out var newHi))
            {
                deadSlots.Add(slot);
                newFormIds.Add(0);
                continue;
            }
            if (newHi != (int)hi) remapped++;
            newFormIds.Add(((uint)newHi << 24) | (formId & 0xFFFFFF));
        }

        var (keptChangeForms, dropped) = FilterChangeForms(save, deadSlots);

        var removedRegular = missingRegular.Count;
        var removedLight = missingLight.Count;
        PrunePlugins(save, missingRegular, missingLight);

        save.FormIds = newFormIds;
        save.ChangeForms = keptChangeForms;
        save.ChangeFormCount -= (uint)dropped;
        save.SetLocation(ApplyPrefix(save.Location, options));

        var bytes = save.Rebuild();
        return new SurgeryOutcome
        {
            Bytes = bytes,
            InGameName = save.Location,
            RemovedRegular = removedRegular,
            RemovedLight = removedLight,
            ZeroedFormIds = deadSlots.Count,
            RemappedFormIds = remapped,
            DroppedChangeForms = dropped,
            OriginalBytes = originalBytes,
            Checks = Verify(bytes, deadSlots)
        };
    }

    /// <summary>Rewrites only the in-game name, leaving the save's contents untouched.</summary>
    public static byte[] Relabel(SaveFile save, string prefix, bool replaceExistingTag = true)
    {
        save.SetLocation(ApplyPrefix(save.Location,
            new SurgeryOptions { Prefix = prefix, ReplaceExistingTag = replaceExistingTag }));
        return save.Rebuild();
    }

    public static string ApplyPrefix(string location, SurgeryOptions options)
    {
        if (string.IsNullOrEmpty(options.Prefix)) return location;
        var body = options.ReplaceExistingTag ? LeadingTag.Replace(location, "") : location;
        return body.StartsWith(options.Prefix, StringComparison.Ordinal) ? body : options.Prefix + body;
    }

    private static Dictionary<int, int> SurvivorMap(int count, IReadOnlySet<int> removed)
    {
        var map = new Dictionary<int, int>(count);
        var next = 0;
        for (var i = 0; i < count; i++)
            if (!removed.Contains(i)) map[i] = next++;
        return map;
    }

    private static void PrunePlugins(SaveFile save, IReadOnlySet<int> missingRegular, IReadOnlySet<int> missingLight)
    {
        var regular = save.Plugins.Where((_, i) => !missingRegular.Contains(i)).ToList();
        var light = save.LightPlugins.Where((_, i) => !missingLight.Contains(i)).ToList();
        save.ReplacePluginLists(regular, light);
    }

    private static (byte[] Kept, int Dropped) FilterChangeForms(SaveFile save, HashSet<int> deadSlots)
    {
        var kept = new MemoryStream(save.ChangeForms.Length);
        var dropped = 0;
        var covered = 0;
        var walked = 0L;

        foreach (var entry in ChangeForms.Walk(save.ChangeForms, save.ChangeFormCount))
        {
            walked++;
            covered = entry.End;
            if (entry.PointsAtFormIdArray && deadSlots.Contains(entry.RefValue))
            {
                dropped++;
                continue;
            }
            kept.Write(save.ChangeForms, entry.Start, entry.Length);
        }

        if (walked != save.ChangeFormCount || covered != save.ChangeForms.Length)
            throw new SaveFormatException(
                $"Change form section does not read cleanly ({walked:N0} of {save.ChangeFormCount:N0} " +
                $"records, {covered:N0} of {save.ChangeForms.Length:N0} bytes). Refusing to write a " +
                "save that would lose data.");

        return (kept.ToArray(), dropped);
    }

    /// <summary>Re-reads the produced file and checks the invariants the game depends on.</summary>
    private static List<Check> Verify(byte[] bytes, HashSet<int> clearedSlots)
    {
        var checks = new List<Check>();
        SaveFile result;
        try
        {
            result = SaveFile.FromBytes(bytes);
        }
        catch (Exception ex)
        {
            checks.Add(new Check("Reads back", false, ex.Message));
            return checks;
        }

        var layout = result.LayoutIssues();
        checks.Add(new Check("Section layout", layout.Count == 0,
            layout.Count == 0 ? "no gaps between sections" : string.Join("; ", layout)));

        var walked = 0L;
        var referencesCleared = 0;
        var referencesAlreadyEmpty = 0;
        foreach (var entry in ChangeForms.Walk(result.ChangeForms, result.ChangeFormCount))
        {
            walked++;
            if (!entry.PointsAtFormIdArray) continue;

            if (entry.RefValue >= result.FormIds.Count || clearedSlots.Contains(entry.RefValue))
                referencesCleared++;
            else if (result.FormIds[entry.RefValue] == 0)
                referencesAlreadyEmpty++;   // the slot was empty before the operation, so it stays
        }
        checks.Add(new Check("Change forms readable", walked == result.ChangeFormCount,
            $"{walked:N0} of {result.ChangeFormCount:N0} records"));
        checks.Add(new Check("Removed content is unreferenced", referencesCleared == 0,
            referencesCleared == 0
                ? "nothing points at a slot this operation cleared"
                : $"{referencesCleared:N0} records still do"));
        if (referencesAlreadyEmpty > 0)
            checks.Add(new Check("Empty slots inherited from the original", true,
                $"{referencesAlreadyEmpty:N0} records point at slots that were already empty - " +
                "left as they were found"));

        var overRegular = result.FormIds.Count(f => (f >> 24) is > 0 and < 0xFE && (f >> 24) >= (uint)result.Plugins.Count);
        var overLight = result.FormIds.Count(f => (f >> 24) == 0xFE && ((f >> 12) & 0xFFF) >= (uint)result.LightPlugins.Count);
        checks.Add(new Check("Plugin indices in range", overRegular == 0 && overLight == 0,
            overRegular == 0 && overLight == 0
                ? $"{result.Plugins.Count} regular + {result.LightPlugins.Count} light"
                : $"{overRegular} regular and {overLight} light forms point past the list"));

        return checks;
    }
}
