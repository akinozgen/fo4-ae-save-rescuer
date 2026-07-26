using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SaveRescuer.Core;

namespace SaveRescuer.App;

public partial class MainWindow : Window
{
    private const string AllCharacters = "Every character";

    private static readonly (string Label, Func<SaveRow, bool> Match)[] StatusFilters =
    [
        ("Needs rescue", row => row.NeedsRescue),
        ("Every save", _ => true),
        ("Rescuable", row => row.Outlook == RescueOutlook.Good),
        ("Heavy", row => row.Outlook is RescueOutlook.Uncertain or RescueOutlook.Risky),
        ("Nothing missing", row => row.Summary.IsReadable && !row.NeedsRescue),
        ("Unreadable", row => !row.Summary.IsReadable)
    ];

    private readonly ObservableCollection<SaveRow> _rows = [];
    private readonly List<SaveRow> _external = [];
    private AppSettings _settings = AppSettings.Load();
    private SaveVault _vault;
    private IReadOnlySet<string> _installed = new HashSet<string>();

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _vault = new SaveVault(_settings.VaultFolder);

        SaveGrid.ItemsSource = _rows;
        StatusFilter.ItemsSource = StatusFilters.Select(f => f.Label).ToList();
        StatusFilter.SelectedIndex = 1;

        Loaded += async (_, _) =>
        {
            PrepareLaunchButton();
            await ReloadAsync();
            if (App.StartupSave is not null) AddExternal(App.StartupSave);
        };
    }

    // ---------- loading ----------

    private async Task ReloadAsync()
    {
        if (!Directory.Exists(_settings.SavesFolder))
        {
            Status("No save folder found - set it under Settings.");
            FolderLabel.Text = _settings.SavesFolder ?? "(save folder not set)";
            return;
        }

        FolderLabel.Text = _settings.SavesFolder;
        Busy.Visibility = Visibility.Visible;
        Status("Reading saves…");

        var folder = _settings.SavesFolder!;
        var dataFolder = _settings.DataFolder;
        var vanilla = _settings.VanillaOnly;
        var externals = _external.Select(r => r.Path).ToList();

        var (rows, installed) = await Task.Run(() =>
        {
            var installed = PluginInventory.ReadInstalled(dataFolder);
            var found = SaveLibrary.Scan(folder)
                                   .Select(s => SaveRow.From(s, installed, vanilla))
                                   .ToList();
            var keep = new HashSet<string>(found.Select(r => r.Path), StringComparer.OrdinalIgnoreCase);
            var extra = externals.Where(p => File.Exists(p) && !keep.Contains(p))
                                 .Select(p => SaveRow.From(SaveSummary.Read(p), installed, vanilla, external: true))
                                 .ToList();
            return (extra.Concat(found).ToList(), installed);
        });

        _installed = installed;
        _external.Clear();
        _external.AddRange(rows.Where(r => r.IsExternal));

        _rows.Clear();
        foreach (var row in rows) _rows.Add(row);

        RefreshCharacterFilter();
        ApplyFilter();
        Busy.Visibility = Visibility.Collapsed;

        var needy = _rows.Count(r => r.NeedsRescue);
        var unreadable = _rows.Count(r => !r.Summary.IsReadable);
        Status(needy == 0
            ? $"{_rows.Count} saves, none of them missing content."
            : $"{_rows.Count} saves, {needy} missing content" + (unreadable > 0 ? $", {unreadable} unreadable." : "."));
    }

    private void RefreshCharacterFilter()
    {
        var previous = CharacterFilter.SelectedItem as string;
        var names = _rows.Select(r => r.Character).Distinct()
                         .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        names.Insert(0, AllCharacters);

        CharacterFilter.ItemsSource = names;
        CharacterFilter.SelectedItem = previous is not null && names.Contains(previous) ? previous : AllCharacters;
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var character = CharacterFilter.SelectedItem as string ?? AllCharacters;
        var status = StatusFilters[Math.Max(StatusFilter.SelectedIndex, 0)];

        var view = CollectionViewSource.GetDefaultView(_rows);
        view.Filter = item => item is SaveRow row
                              && row.Matches(query)
                              && (character == AllCharacters || row.Character == character)
                              && status.Match(row);
        view.Refresh();

        var shown = view.Cast<SaveRow>().Count();
        CountLabel.Text = shown == _rows.Count
            ? $"{shown} saves"
            : $"{shown} of {_rows.Count} saves shown";
    }

    private void AddExternal(string path)
    {
        var existing = _rows.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = SaveRow.From(SaveSummary.Read(path), _installed, _settings.VanillaOnly, external: true);
            _external.Add(existing);
            _rows.Insert(0, existing);
            RefreshCharacterFilter();
        }

        SearchBox.Clear();
        StatusFilter.SelectedIndex = 1;               // show everything, or the drop may be filtered away
        ApplyFilter();
        SaveGrid.SelectedItem = existing;
        SaveGrid.ScrollIntoView(existing);
        Status($"Opened {existing.FileName}");
    }

    // ---------- detail pane ----------

    private SaveRow? Current => SaveGrid.SelectedItem as SaveRow;
    private List<SaveRow> Selection => SaveGrid.SelectedItems.Cast<SaveRow>().ToList();

    private void OnSaveSelected(object sender, SelectionChangedEventArgs e)
    {
        var row = Current;
        DetailContent.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        if (row is null) return;

        var summary = row.Summary;
        DetailCharacter.Text = row.Character + (row.IsExternal ? "  (outside the save folder)" : "");
        DetailName.Text = summary.IsReadable ? summary.Location : summary.Error;

        ChipLevel.Text = summary.IsReadable ? $"level {summary.Level}" : "unreadable";
        ChipPlayTime.Text = row.PlayTimeShort.Length > 0 ? row.PlayTimeShort : "-";
        ChipVersion.Text = summary.IsReadable ? $"{summary.GameVersion} · form {summary.FormVersion}" : "-";
        ChipSize.Text = row.Size;

        StatChangeForms.Text = row.ChangeForms;
        StatFormIds.Text = summary.IsReadable ? $"{summary.FormIdCount:N0}" : "-";

        OutlookDot.Fill = row.StatusBrush;
        OutlookTitle.Text = OutlookTitleFor(row);
        OutlookNote.Text = OutlookNoteFor(row);

        var missing = row.Plugins?.MissingCount ?? 0;
        PluginCaption.Text = summary.IsReadable
            ? $"PLUGINS · {summary.PluginCount} REFERENCED · {missing} MISSING"
            : "PLUGINS";
        ShowPlugins(row);

        RescueButton.IsEnabled = row.NeedsRescue;
        RescueButton.Content = Selection.Count > 1
            ? $"Rescue {Selection.Count} saves"
            : row.Summary.IsReadable
                ? row.NeedsRescue ? "Rescue this save" : "Nothing to remove"
                : "Cannot read this save";
        if (Selection.Count > 1) RescueButton.IsEnabled = Selection.Any(r => r.NeedsRescue);

        var shot = Screenshots.Load(summary);
        Shot.Source = shot;
        NoShot.Visibility = shot is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPlugins(SaveRow row)
    {
        var entries = row.Plugins?.Entries ?? [];
        var rows = entries.Where(e => MissingOnly.IsChecked != true || e.State == PluginState.Missing)
                          .Select(e => new PluginRow(e))
                          .ToList();
        PluginList.ItemsSource = rows;
    }

    private void OnMissingOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (Current is { } row) ShowPlugins(row);
    }

    private static string OutlookTitleFor(SaveRow row) => !row.Summary.IsReadable
        ? "Cannot be read"
        : row.Outlook switch
        {
            RescueOutlook.NothingToDo => "Nothing missing",
            RescueOutlook.Good => "Good candidate",
            RescueOutlook.Uncertain => "Heavy save",
            RescueOutlook.Risky => "Very heavy save",
            _ => ""
        };

    private static string OutlookNoteFor(SaveRow row)
    {
        if (!row.Summary.IsReadable) return row.Summary.Error ?? "";

        var missing = row.MissingCount;
        return row.Outlook switch
        {
            RescueOutlook.NothingToDo =>
                "Every plugin this save refers to is installed, so there is nothing to cut out.",
            RescueOutlook.Good =>
                $"{missing} plugin{(missing == 1 ? "" : "s")} gone, {row.Summary.ChangeFormCount:N0} change forms. " +
                "Saves this size have come back reliably.",
            RescueOutlook.Uncertain =>
                $"{missing} plugin{(missing == 1 ? "" : "s")} gone, {row.Summary.ChangeFormCount:N0} change forms - " +
                "heavier than any save confirmed working, lighter than the one that failed. Worth trying.",
            _ =>
                $"{row.Summary.ChangeFormCount:N0} change forms. A save this heavy has been seen to load and then " +
                "reject the next save written over it. Try it, but keep expectations low.",
        };
    }

    // ---------- actions ----------

    private async void OnRescueClick(object sender, RoutedEventArgs e)
    {
        var targets = Selection.Where(r => r.NeedsRescue).ToList();
        if (targets.Count == 0)
        {
            Status("Nothing to rescue in the current selection.");
            return;
        }

        var window = new RescueWindow(targets, _settings, _vault) { Owner = this };
        window.ShowDialog();
        if (window.AnythingWritten) await ReloadAsync();
    }

    private void OnRelabelClick(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || !row.Summary.IsReadable) return;

        var window = new TagWindow(row.Summary.Location, _settings.Prefix) { Owner = this };
        if (window.ShowDialog() != true) return;

        var report = RescueWorkflow.Relabel(row.Path, _settings.SavesFolder!, window.Prefix);
        Status(report.Succeeded
            ? $"Wrote {report.OutputName} with the in-game name tagged."
            : $"Could not write the tagged copy: {report.Error}");
        if (report.Succeeded) _ = ReloadAsync();
    }

    private void OnBackupSelectedClick(object sender, RoutedEventArgs e)
    {
        var targets = Selection;
        if (targets.Count == 0) return;

        var kept = 0;
        foreach (var row in targets)
        {
            try
            {
                _vault.Backup(row.Path, "kept by hand");
                kept++;
            }
            catch (Exception ex)
            {
                Status($"Could not back up {row.FileName}: {ex.Message}");
                return;
            }
        }
        Status($"{kept} save{(kept == 1 ? "" : "s")} copied into the backup folder.");
    }

    private void OnBackupsClick(object sender, RoutedEventArgs e)
    {
        new BackupsWindow(_vault, _settings) { Owner = this }.ShowDialog();
        _ = ReloadAsync();
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings) { Owner = this };
        if (window.ShowDialog() != true) return;

        _settings = window.Result;
        _settings.Save();
        _vault = new SaveVault(_settings.VaultFolder);
        PrepareLaunchButton();
        await ReloadAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Current is { NeedsRescue: true }) OnRescueClick(sender, e);
    }

    private void OnSelectNeedyClick(object sender, RoutedEventArgs e)
    {
        SaveGrid.SelectedItems.Clear();
        foreach (var row in CollectionViewSource.GetDefaultView(_rows).Cast<SaveRow>().Where(r => r.NeedsRescue))
            SaveGrid.SelectedItems.Add(row);
        Status($"{SaveGrid.SelectedItems.Count} saves selected.");
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => Reveal(_settings.SavesFolder);

    private void OnShowInFolderClick(object sender, RoutedEventArgs e)
    {
        if (Current is { } row) Reveal(row.Path, select: true);
    }

    private static void Reveal(string? path, bool select = false)
    {
        if (path is null || (!select && !Directory.Exists(path)) || (select && !File.Exists(path))) return;
        Process.Start(new ProcessStartInfo("explorer.exe", select ? $"/select,\"{path}\"" : $"\"{path}\"")
        {
            UseShellExecute = true
        });
    }

    // ---------- launching the game ----------

    private void PrepareLaunchButton()
    {
        var options = GameLauncher.Discover(_settings.DataFolder);
        LaunchButton.IsEnabled = options.Count > 0;
        LaunchIcon.Source = IconExtractor.FromExecutable(GameLauncher.IconSource(_settings.DataFolder));
        LaunchButton.ToolTip = options.Count == 0
            ? "No game executable found - check the Data folder under Settings"
            : $"Start with {options[0].DisplayName}";

        var menu = new ContextMenu();
        foreach (var option in options)
        {
            var item = new MenuItem { Header = option.DisplayName, Tag = option };
            item.Click += (_, _) => Launch(option);
            menu.Items.Add(item);
        }
        LaunchButton.ContextMenu = options.Count > 0 ? menu : null;
    }

    private void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        if (LaunchButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = LaunchButton;
        menu.IsOpen = true;
    }

    private void Launch(LaunchOption option)
    {
        try
        {
            GameLauncher.Launch(option);
            Status($"Started {option.FileName}.");
        }
        catch (Exception ex)
        {
            Status($"Could not start {option.FileName}: {ex.Message}");
        }
    }

    // ---------- drag and drop ----------

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedSaves(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var saves = DroppedSaves(e);
        foreach (var path in saves) AddExternal(path);
        if (saves.Count == 0) Status("That was not a .fos save file.");
    }

    private static List<string> DroppedSaves(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(p => p.EndsWith(".fos", StringComparison.OrdinalIgnoreCase) && File.Exists(p)).ToList()
            : [];

    private void Status(string message) => StatusLabel.Text = message;
}
