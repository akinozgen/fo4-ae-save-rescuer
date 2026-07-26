using System.Text.RegularExpressions;

namespace SaveRescuer.Core;

/// <summary>
/// Save file names. The game reads the slot number from the name, so a rescued save has to be
/// written into a manual slot: anything called Autosave or Quicksave gets rotated away.
///
/// Old saves use "ID M HEXNAME" where newer ones use "ID _ HEXNAME", and a Survival save carries
/// an S in the same place. Both are normalised to the current shape.
/// </summary>
public static partial class SaveNaming
{
    // After the character ID comes either the old single-letter marker or the current underscore.
    // The marker is restricted to non-hex letters, or it would eat a digit of the name that follows.
    [GeneratedRegex(@"^(?<type>[A-Za-z]+\d*)_(?<id>[0-9A-Fa-f]{8})(?:(?<marker>[G-Zg-z])|_)(?<hex>[0-9A-Fa-f]*)_(?<rest>.+)$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^Save(?<slot>\d+)_", RegexOptions.IgnoreCase)]
    private static partial Regex SlotPattern();

    /// <summary>The single letter some names carry after the character ID.</summary>
    public static string? Marker(string fileName)
    {
        var m = NamePattern().Match(Path.GetFileNameWithoutExtension(fileName));
        return m.Success && m.Groups["marker"].Success ? m.Groups["marker"].Value : null;
    }

    /// <summary>True when the name marks a Survival save, where the game blocks manual saving.</summary>
    public static bool IsSurvival(string fileName) =>
        string.Equals(Marker(fileName), "S", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name to write a rescued save under: the current naming shape in manual slot
    /// <paramref name="slot"/>, keeping the character ID, location and timestamp intact.
    /// </summary>
    public static string ForSlot(string originalFileName, int slot)
    {
        var stem = Path.GetFileNameWithoutExtension(originalFileName);
        var m = NamePattern().Match(stem);
        if (!m.Success) return $"Save{slot}_{stem}.fos";
        return $"Save{slot}_{m.Groups["id"].Value}_{m.Groups["hex"].Value}_{m.Groups["rest"].Value}.fos";
    }

    /// <summary>Slot number a file name claims, if any.</summary>
    public static int? SlotOf(string fileName)
    {
        var m = SlotPattern().Match(Path.GetFileName(fileName));
        return m.Success && int.TryParse(m.Groups["slot"].Value, out var n) ? n : null;
    }

    /// <summary>
    /// The first manual slot number not in use in <paramref name="savesFolder"/>, at or above
    /// <paramref name="startAt"/>. Rescued saves are parked high so they do not collide with the
    /// numbers the game hands out.
    /// </summary>
    public static int NextFreeSlot(string? savesFolder, int startAt = 900)
    {
        var used = UsedSlots(savesFolder);
        var slot = startAt;
        while (used.Contains(slot)) slot++;
        return slot;
    }

    public static HashSet<int> UsedSlots(string? savesFolder)
    {
        var used = new HashSet<int>();
        if (savesFolder is null || !Directory.Exists(savesFolder)) return used;
        foreach (var file in Directory.EnumerateFiles(savesFolder, "*.fos"))
            if (SlotOf(file) is { } slot) used.Add(slot);
        return used;
    }

    /// <summary>A path in <paramref name="folder"/> that no file occupies yet.</summary>
    public static string Unique(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
