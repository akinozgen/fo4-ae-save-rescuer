using System.Windows.Media;
using SaveRescuer.Core;

namespace SaveRescuer.App;

/// <summary>One line in the save list.</summary>
public sealed class SaveRow
{
    public required SaveSummary Summary { get; init; }
    public required PluginInventory? Plugins { get; init; }
    /// <summary>A save opened from outside the save folder, e.g. dropped onto the window.</summary>
    public bool IsExternal { get; init; }

    public string Path => Summary.Path;
    public string FileName => Summary.FileName;
    public string Character => string.IsNullOrWhiteSpace(Summary.PlayerName) ? "(unnamed)" : Summary.PlayerName;
    public string Level => Summary.IsReadable ? Summary.Level.ToString() : "-";
    public string InGameName => Summary.Location;
    public string PlayTime => Summary.PlayTime;

    /// <summary>
    /// The save stores play time twice in one string ("2d.3h.51m.2 days.3 hours.51 minutes");
    /// only the compact half is worth showing.
    /// </summary>
    public string PlayTimeShort
    {
        get
        {
            var match = System.Text.RegularExpressions.Regex.Match(Summary.PlayTime, @"^\d+d\.\d+h\.\d+m");
            return match.Success ? match.Value : Summary.PlayTime;
        }
    }
    public string Size => $"{Summary.MegaBytes:N1} MB";
    public string ChangeForms => Summary.IsReadable ? $"{Summary.ChangeFormCount:N0}" : "-";
    public string Written => Summary.WrittenAt.ToString("yyyy-MM-dd HH:mm");
    public int MissingCount => Plugins?.MissingCount ?? 0;

    public string MissingLabel => !Summary.IsReadable ? "?" : MissingCount == 0 ? "-" : MissingCount.ToString();

    public RescueOutlook? Outlook => Plugins is null
        ? null
        : MissingCount == 0 ? RescueOutlook.NothingToDo
        : Summary.ChangeFormCount < SaveAnalysis.MeasuredGoodCeiling ? RescueOutlook.Good
        : Summary.ChangeFormCount < SaveAnalysis.MeasuredRiskFloor ? RescueOutlook.Uncertain
        : RescueOutlook.Risky;

    public bool NeedsRescue => MissingCount > 0;

    public string StatusText => !Summary.IsReadable
        ? "unreadable"
        : Outlook switch
        {
            RescueOutlook.NothingToDo => "complete",
            RescueOutlook.Good => "rescuable",
            RescueOutlook.Uncertain => "heavy",
            RescueOutlook.Risky => "very heavy",
            _ => ""
        };

    public Brush StatusBrush => new SolidColorBrush(!Summary.IsReadable
        ? Color.FromRgb(0xE4, 0x60, 0x5E)
        : Outlook switch
        {
            RescueOutlook.NothingToDo => Color.FromRgb(0x6F, 0xA8, 0xDC),
            RescueOutlook.Good => Color.FromRgb(0x54, 0xC0, 0x8A),
            RescueOutlook.Uncertain => Color.FromRgb(0xE8, 0xA3, 0x3D),
            RescueOutlook.Risky => Color.FromRgb(0xE4, 0x60, 0x5E),
            _ => Color.FromRgb(0x8C, 0x95, 0xA6)
        });

    /// <summary>Free-text match used by the search box.</summary>
    public bool Matches(string query) =>
        query.Length == 0 ||
        Character.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        InGameName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        FileName.Contains(query, StringComparison.OrdinalIgnoreCase);

    public static SaveRow From(SaveSummary summary, IReadOnlySet<string> installed, bool vanillaOnly, bool external = false) =>
        new()
        {
            Summary = summary,
            Plugins = summary.IsReadable ? PluginInventory.Build(summary, installed, vanillaOnly) : null,
            IsExternal = external
        };
}

/// <summary>One line in the plugin table of the detail pane.</summary>
public sealed class PluginRow(PluginEntry entry)
{
    public string Index => entry.IndexLabel;
    public string Name => entry.Name;
    public PluginState State => entry.State;

    public string StateText => entry.State switch
    {
        PluginState.BaseGame => "base game",
        PluginState.Installed => "installed",
        _ => "MISSING"
    };

    public Brush StateBrush => new SolidColorBrush(entry.State switch
    {
        PluginState.BaseGame => Color.FromRgb(0x6F, 0xA8, 0xDC),
        PluginState.Installed => Color.FromRgb(0x54, 0xC0, 0x8A),
        _ => Color.FromRgb(0xE4, 0x60, 0x5E)
    });

    public bool IsMissing => entry.State == PluginState.Missing;
}
