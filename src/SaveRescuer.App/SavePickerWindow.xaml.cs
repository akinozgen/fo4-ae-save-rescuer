using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using SaveRescuer.Core;
using SaveRescuer.Core.Models;

namespace SaveRescuer.App;

public partial class SavePickerWindow : Window
{
    private readonly ObservableCollection<SaveRow> _rows = [];

    public string? SelectedPath { get; private set; }

    public SavePickerWindow(string savesFolder, RescueService service)
    {
        InitializeComponent();
        Grid.ItemsSource = _rows;

        int withMissing = 0;
        foreach (var save in RescueService.EnumerateSaves(savesFolder))
        {
            int missing = -1;
            if (service.Environment.IsComplete)
            {
                missing = service.Diagnose(save).MissingCount;
                if (missing > 0) withMissing++;
            }
            _rows.Add(new SaveRow(save, missing));
        }

        Header.Text = $"{_rows.Count} save(s) in {savesFolder}" +
                      (withMissing > 0 ? $"   -   {withMissing} need rescuing" : "");
    }

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Accept();

    private void BtnOk_Click(object sender, RoutedEventArgs e) => Accept();

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Accept()
    {
        if (Grid.SelectedItem is not SaveRow row) return;
        SelectedPath = row.FullPath;
        DialogResult = true;
    }
}

public sealed class SaveRow(SaveInfo save, int missing)
{
    public string Character { get; } = save.CharacterName.Length > 0 ? save.CharacterName : "(unnamed)";
    public uint Level { get; } = save.CharacterLevel;
    public string Location { get; } = save.Location;
    public string PlayTime { get; } = save.PlayTime;
    public string Plugins { get; } = $"{save.Plugins.Count}+{save.LightPlugins.Count}";
    public string FileName { get; } = save.FileName;
    public string FullPath { get; } = save.FilePath;

    public string Missing { get; } = missing < 0 ? "?" : missing == 0 ? "-" : missing.ToString();

    public Brush MissingBrush { get; } = missing > 0
        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))
        : new SolidColorBrush(Color.FromRgb(0x5A, 0x7A, 0x63));
}
