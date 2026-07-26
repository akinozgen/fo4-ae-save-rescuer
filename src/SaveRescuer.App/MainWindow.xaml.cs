using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SaveRescuer.Core;
using SaveRescuer.Core.Models;

namespace SaveRescuer.App;

public partial class MainWindow : Window
{
    private readonly GameEnvironment _env;
    private RescueService _service;
    private SaveInfo? _save;
    private readonly ObservableCollection<PluginRow> _rows = [];

    public MainWindow()
    {
        InitializeComponent();
        PluginGrid.ItemsSource = _rows;

        _env = GameEnvironment.Detect();
        _service = new RescueService(_env);
        ShowPaths();

        Log("Fallout 4 AE Save Rescuer ready.");
        Log(_env.DataFolder is null
            ? "Game folder not found automatically - set it with the 'change' button."
            : $"Game data: {_env.DataFolder}");
        Log(_env.PluginsTxt is null
            ? "Load order file not found - set it with the 'change' button."
            : $"Load order: {_env.PluginsTxt}");

        var manifest = _service.LoadManifest();
        if (manifest is not null)
            Log($"A previous rescue from {manifest.CreatedAt:g} ({manifest.Stubs.Count} placeholders) can be undone.");
    }

    // ---------- loading a save ----------

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasSaveFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var save = files.FirstOrDefault(f => f.EndsWith(".fos", StringComparison.OrdinalIgnoreCase));
            if (save is not null) LoadSave(save);
        }
    }

    private static bool HasSaveFile(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) &&
        e.Data.GetData(DataFormats.FileDrop) is string[] files &&
        files.Any(f => f.EndsWith(".fos", StringComparison.OrdinalIgnoreCase));

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Fallout 4 save (*.fos)|*.fos|All files|*.*",
            InitialDirectory = _env.SavesFolder ?? ""
        };
        if (dlg.ShowDialog() == true) LoadSave(dlg.FileName);
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (_env.SavesFolder is null || !Directory.Exists(_env.SavesFolder))
        {
            Log("Save folder not found. Use 'Open save file...' instead.");
            return;
        }

        var picker = new SavePickerWindow(_env.SavesFolder, _service) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPath is not null)
            LoadSave(picker.SelectedPath);
    }

    private void LoadSave(string path)
    {
        try
        {
            _save = SaveReader.Read(path);
        }
        catch (Exception ex)
        {
            Log($"Could not read {Path.GetFileName(path)}: {ex.Message}");
            return;
        }

        SaveTitle.Text = $"{(_save.CharacterName.Length > 0 ? _save.CharacterName : "(unnamed)")}  -  Level {_save.CharacterLevel}";
        SaveMeta.Text = $"{_save.Location}   |   {_save.PlayTime}   |   game {_save.GameVersion}   |   " +
                        $"{_save.Plugins.Count} plugins + {_save.LightPlugins.Count} light   |   {_save.FileName}";
        ShowScreenshot(_save);
        Log($"Loaded {_save.FileName}");
        Analyse();
    }

    private void ShowScreenshot(SaveInfo save)
    {
        ShotImage.Source = null;
        ShotPlaceholder.Visibility = Visibility.Visible;
        if (save.ScreenshotRgba is null || save.ScreenshotWidth <= 0) return;

        // Save data is RGBA; WPF wants BGRA.
        var src = save.ScreenshotRgba;
        var bgra = new byte[src.Length];
        for (int i = 0; i + 3 < src.Length; i += 4)
        {
            bgra[i] = src[i + 2];
            bgra[i + 1] = src[i + 1];
            bgra[i + 2] = src[i];
            bgra[i + 3] = 255;
        }

        var bmp = BitmapSource.Create(save.ScreenshotWidth, save.ScreenshotHeight, 96, 96,
            PixelFormats.Bgra32, null, bgra, save.ScreenshotWidth * 4);
        ShotImage.Source = bmp;
        ShotPlaceholder.Visibility = Visibility.Collapsed;
    }

    // ---------- diagnosis ----------

    private void Analyse()
    {
        _rows.Clear();
        if (_save is null) return;

        if (!_env.IsComplete)
        {
            SaveVerdict.Text = "Set the game Data folder and load order file before continuing.";
            SaveVerdict.Foreground = (Brush)FindResource("Amber");
            BtnPreview.IsEnabled = BtnRescue.IsEnabled = false;
            return;
        }

        var diagnosis = _service.Diagnose(_save);
        int i = 1;
        foreach (var p in diagnosis.Plugins)
            _rows.Add(new PluginRow(i++, p));

        if (diagnosis.CanLoadCleanly)
        {
            SaveVerdict.Text = "This save has everything it needs - it should load without a warning.";
            SaveVerdict.Foreground = (Brush)FindResource("Ink");
            BtnRescue.IsEnabled = false;
            BtnPreview.IsEnabled = false;
        }
        else
        {
            SaveVerdict.Text = $"{diagnosis.MissingCount} plugin(s) missing, {diagnosis.NotEnabledCount} present but not enabled. " +
                               "Rescue will create empty placeholders and enable the load-order entries.";
            SaveVerdict.Foreground = (Brush)FindResource("Amber");
            BtnRescue.IsEnabled = true;
            BtnPreview.IsEnabled = true;
        }

        Log($"Diagnosis: {diagnosis.MissingCount} missing, {diagnosis.NotEnabledCount} not enabled, " +
            $"{diagnosis.Stubs.Count()} existing placeholder(s).");
    }

    // ---------- actions ----------

    private void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_save is null) return;
        var result = _service.Rescue(_save, dryRun: true);
        Log("--- preview ---");
        foreach (var s in result.CreatedStubs) Log($"  would create placeholder: {s}");
        foreach (var s in result.EnabledPlugins) Log($"  would enable in load order: {s}");
        foreach (var m in result.Messages) Log($"  {m}");
    }

    private void BtnRescue_Click(object sender, RoutedEventArgs e)
    {
        if (_save is null) return;

        var confirm = MessageBox.Show(
            "Empty placeholder plugins will be created for the missing content and the load order " +
            "will be updated so this save can be opened.\n\n" +
            "Items from those missing mods will simply be gone from the save. " +
            "A backup of the load order is kept and everything can be undone.\n\nContinue?",
            "Rescue this save", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var result = _service.Rescue(_save);
            Log("--- rescue ---");
            foreach (var s in result.CreatedStubs) Log($"  placeholder created: {s}");
            foreach (var s in result.EnabledPlugins) Log($"  enabled: {s}");
            if (result.LoadOrderBackup is not null) Log($"  load order backup: {result.LoadOrderBackup}");
            foreach (var m in result.Messages) Log($"  {m}");
            Analyse();
            Log("Done. Start the game and load the save.");
        }
        catch (Exception ex)
        {
            Log($"Rescue failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Rescue failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnRevert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _service.Revert();
            Log("--- undo ---");
            foreach (var s in result.RemovedStubs) Log($"  placeholder removed: {s}");
            foreach (var m in result.Messages) Log($"  {m}");
            if (!result.AnyChange && result.Messages.Count == 0) Log("  nothing to undo");
            if (_save is not null) Analyse();
        }
        catch (Exception ex)
        {
            Log($"Undo failed: {ex.Message}");
        }
    }

    private void BtnPaths_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PathsWindow(_env) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        _service = new RescueService(_env);
        ShowPaths();
        Log($"Paths updated. Data: {_env.DataFolder}   Load order: {_env.PluginsTxt}");
        if (_save is not null) Analyse();
    }

    private void ShowPaths()
    {
        PathData.Text = _env.DataFolder ?? "(not set)";
        PathOrder.Text = _env.PluginsTxt ?? "(not set)";
    }

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}

/// <summary>One row in the plugin table.</summary>
public sealed class PluginRow(int index, PluginStatus status)
{
    public int Index { get; } = index;
    public string Name { get; } = status.Name;
    public string Kind { get; } = status.IsLight ? "light" : "normal";

    public string StatusText { get; } = status.State switch
    {
        PluginState.Ok => "ok",
        PluginState.BaseGame => "base game",
        PluginState.NotEnabled => "not enabled",
        PluginState.Missing => "MISSING",
        PluginState.Stub => "placeholder",
        _ => "?"
    };

    public Brush StatusBrush { get; } = status.State switch
    {
        PluginState.Missing => new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
        PluginState.NotEnabled => new SolidColorBrush(Color.FromRgb(0xFF, 0xB6, 0x42)),
        PluginState.Stub => new SolidColorBrush(Color.FromRgb(0x8F, 0xD9, 0xB4)),
        PluginState.BaseGame => new SolidColorBrush(Color.FromRgb(0x5A, 0x7A, 0x63)),
        _ => new SolidColorBrush(Color.FromRgb(0x48, 0xFF, 0x9E))
    };
}
