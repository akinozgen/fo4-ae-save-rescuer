using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using SaveRescuer.Core;

namespace SaveRescuer.App;

/// <summary>What is planned for one save, and what came of it.</summary>
public sealed class PlanRow(SaveRow source) : INotifyPropertyChanged
{
    private string _pluginsOut = "…";
    private string _formsCleared = "…";
    private string _recordsDropped = "…";
    private string _result = "not started";
    private Brush _stateBrush = new SolidColorBrush(Color.FromRgb(0x8C, 0x95, 0xA6));

    public SaveRow Source { get; } = source;
    public string Character => Source.Character;
    public string InGameName => Source.InGameName;

    public string PluginsOut { get => _pluginsOut; set => Set(ref _pluginsOut, value); }
    public string FormsCleared { get => _formsCleared; set => Set(ref _formsCleared, value); }
    public string RecordsDropped { get => _recordsDropped; set => Set(ref _recordsDropped, value); }
    public string Result { get => _result; set => Set(ref _result, value); }
    public Brush StateBrush { get => _stateBrush; set => Set(ref _stateBrush, value); }

    public void MarkPlanned(SaveAnalysis analysis)
    {
        PluginsOut = analysis.Plugins.MissingCount.ToString();
        FormsCleared = $"{analysis.DeadFormIdSlots.Count:N0}";
        RecordsDropped = $"{analysis.DeadChangeForms:N0}";
        Result = analysis.IsIntact ? "ready" : "save reads inconsistently";
        StateBrush = new SolidColorBrush(analysis.IsIntact
            ? Color.FromRgb(0x8C, 0x95, 0xA6)
            : Color.FromRgb(0xE4, 0x60, 0x5E));
    }

    public void MarkDone(RescueReport report)
    {
        if (report.Succeeded && report.Outcome is { } outcome)
        {
            PluginsOut = (outcome.RemovedRegular + outcome.RemovedLight).ToString();
            FormsCleared = $"{outcome.ZeroedFormIds:N0}";
            RecordsDropped = $"{outcome.DroppedChangeForms:N0}";
            Result = report.OutputName ?? "written";
            StateBrush = new SolidColorBrush(Color.FromRgb(0x54, 0xC0, 0x8A));
        }
        else
        {
            Result = report.Error ?? "failed";
            StateBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0x60, 0x5E));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed record CheckRow(string Mark, string Name, string Detail, Brush Brush);

public partial class RescueWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SaveVault _vault;
    private readonly ObservableCollection<PlanRow> _plan = [];

    /// <summary>Whether any file was produced, so the caller knows to re-read the folder.</summary>
    public bool AnythingWritten { get; private set; }

    public RescueWindow(IEnumerable<SaveRow> targets, AppSettings settings, SaveVault vault)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _settings = settings;
        _vault = vault;

        foreach (var row in targets) _plan.Add(new PlanRow(row));
        PlanGrid.ItemsSource = _plan;

        PrefixBox.Text = settings.Prefix;
        VanillaBox.IsChecked = settings.VanillaOnly;
        BackupBox.IsChecked = settings.BackupBeforeRescue;
        ReplaceTagBox.IsChecked = settings.ReplaceExistingTag;

        Headline.Text = _plan.Count == 1
            ? $"Rescue {_plan[0].Character}'s save"
            : $"Rescue {_plan.Count} saves";

        UpdatePreview();
        Loaded += async (_, _) => await PlanAsync();
    }

    private SurgeryOptions Options => new()
    {
        Prefix = PrefixBox.Text,
        ReplaceExistingTag = ReplaceTagBox.IsChecked == true,
        VanillaOnly = VanillaBox.IsChecked == true
    };

    /// <summary>
    /// Reads each save fully to count what would come out. Skipped for large batches, where the
    /// numbers are reported as the work is done instead.
    /// </summary>
    private async Task PlanAsync()
    {
        if (_plan.Count > 12)
        {
            foreach (var row in _plan)
            {
                row.PluginsOut = row.Source.MissingCount.ToString();
                row.FormsCleared = "-";
                row.RecordsDropped = "-";
                row.Result = "ready";
            }
            Status($"{_plan.Count} saves queued. Counts are reported as each one is written.");
            return;
        }

        Busy.Visibility = Visibility.Visible;
        Status("Reading the saves…");
        var options = Options;
        var dataFolder = _settings.DataFolder;

        foreach (var row in _plan)
        {
            var path = row.Source.Path;
            try
            {
                var analysis = await Task.Run(() => RescueWorkflow.Diagnose(path, dataFolder, options.VanillaOnly));
                row.MarkPlanned(analysis);
            }
            catch (Exception ex)
            {
                row.Result = ex.Message;
                row.StateBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0x60, 0x5E));
            }
        }

        Busy.Visibility = Visibility.Collapsed;
        var ready = _plan.Count(r => r.Result == "ready");
        Status(ready == _plan.Count
            ? "Ready. Nothing has been written yet."
            : $"{ready} of {_plan.Count} saves can be operated on.");
    }

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        Busy.Visibility = Visibility.Visible;
        ChecksCard.Visibility = Visibility.Collapsed;

        var options = Options;
        var backup = BackupBox.IsChecked == true;
        var outputFolder = _settings.SavesFolder!;
        var slot = SaveNaming.NextFreeSlot(outputFolder, _settings.SlotStart);
        var written = 0;

        foreach (var row in _plan)
        {
            Status($"Working on {row.Source.FileName}…");
            var request = new RescueRequest
            {
                SourcePath = row.Source.Path,
                OutputFolder = outputFolder,
                DataFolder = _settings.DataFolder,
                Options = options,
                BackupFirst = backup,
                Slot = slot
            };

            var report = await Task.Run(() => RescueWorkflow.Rescue(request, _vault));
            row.MarkDone(report);

            if (report.Succeeded)
            {
                written++;
                slot++;
                AnythingWritten = true;
                ShowChecks(report);
            }
        }

        Busy.Visibility = Visibility.Collapsed;
        RunButton.IsEnabled = true;
        RunButton.Content = "Run again";
        Status(written == _plan.Count
            ? $"{written} save{(written == 1 ? "" : "s")} written. Load one from the in-game menu to try it."
            : $"{written} of {_plan.Count} written - the rest are explained in the Result column.");
    }

    private void ShowChecks(RescueReport report)
    {
        if (report.Outcome is not { } outcome) return;

        ChecksList.ItemsSource = outcome.Checks.Select(c => new CheckRow(
            c.Passed ? "ok" : "!!", c.Name, c.Detail,
            new SolidColorBrush(c.Passed ? Color.FromRgb(0x54, 0xC0, 0x8A) : Color.FromRgb(0xE4, 0x60, 0x5E)))).ToList();
        ChecksCard.Visibility = Visibility.Visible;
    }

    private void OnPrefixChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private async void OnVanillaChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        foreach (var row in _plan)
        {
            row.PluginsOut = "…";
            row.FormsCleared = "…";
            row.RecordsDropped = "…";
        }
        await PlanAsync();
    }

    private void UpdatePreview()
    {
        if (_plan.Count == 0) return;
        var sample = SaveSurgeon.ApplyPrefix(_plan[0].InGameName, Options);
        PrefixPreview.Text = $"load menu will read:  {sample}";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void Status(string message) => StatusLabel.Text = message;
}
