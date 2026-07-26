namespace SaveRescuer.Core;

/// <summary>Reads a folder of saves into summaries, newest first.</summary>
public static class SaveLibrary
{
    public static List<SaveSummary> Scan(string? folder, CancellationToken token = default)
    {
        if (folder is null || !Directory.Exists(folder)) return [];

        var files = Directory.EnumerateFiles(folder, "*.fos").ToList();
        var summaries = new SaveSummary[files.Count];
        Parallel.For(0, files.Count, new ParallelOptions { CancellationToken = token },
            i => summaries[i] = SaveSummary.Read(files[i]));

        return summaries.OrderByDescending(s => s.WrittenAt).ToList();
    }

    /// <summary>Saves grouped by character, each group newest first.</summary>
    public static List<IGrouping<string, SaveSummary>> ByCharacter(IEnumerable<SaveSummary> saves) =>
        saves.GroupBy(s => string.IsNullOrWhiteSpace(s.PlayerName) ? "(unnamed)" : s.PlayerName)
             .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
             .ToList();
}
