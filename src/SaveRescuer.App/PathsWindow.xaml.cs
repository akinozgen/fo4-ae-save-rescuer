using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SaveRescuer.Core;

namespace SaveRescuer.App;

public partial class PathsWindow : Window
{
    private readonly GameEnvironment _env;

    public PathsWindow(GameEnvironment env)
    {
        InitializeComponent();
        _env = env;
        Fill();
    }

    private void Fill()
    {
        TxtData.Text = _env.DataFolder ?? "";
        TxtOrder.Text = _env.PluginsTxt ?? "";
        TxtSaves.Text = _env.SavesFolder ?? "";

        CmbCandidates.ItemsSource = _env.PluginsTxtCandidates;
        if (_env.PluginsTxt is not null && _env.PluginsTxtCandidates.Contains(_env.PluginsTxt))
            CmbCandidates.SelectedItem = _env.PluginsTxt;
    }

    private void CmbCandidates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbCandidates.SelectedItem is string path) TxtOrder.Text = path;
    }

    private void BrowseData_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select the game's Data folder", InitialDirectory = TxtData.Text };
        if (dlg.ShowDialog() == true) TxtData.Text = dlg.FolderName;
    }

    private void BrowseOrder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select plugins.txt",
            Filter = "plugins.txt|plugins.txt|Text files|*.txt|All files|*.*",
            InitialDirectory = Path.GetDirectoryName(TxtOrder.Text) ?? ""
        };
        if (dlg.ShowDialog() == true) TxtOrder.Text = dlg.FileName;
    }

    private void BrowseSaves_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select your saves folder", InitialDirectory = TxtSaves.Text };
        if (dlg.ShowDialog() == true) TxtSaves.Text = dlg.FolderName;
    }

    private void Redetect_Click(object sender, RoutedEventArgs e)
    {
        var fresh = GameEnvironment.Detect();
        _env.DataFolder = fresh.DataFolder;
        _env.PluginsTxt = fresh.PluginsTxt;
        _env.SavesFolder = fresh.SavesFolder;
        _env.PluginsTxtCandidates.Clear();
        _env.PluginsTxtCandidates.AddRange(fresh.PluginsTxtCandidates);
        Fill();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var data = TxtData.Text.Trim();
        var order = TxtOrder.Text.Trim();

        if (data.Length > 0 && !Directory.Exists(data))
        {
            MessageBox.Show("That Data folder does not exist.", "Paths", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (order.Length > 0 && !File.Exists(order))
        {
            MessageBox.Show("That load order file does not exist.", "Paths", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _env.DataFolder = data.Length > 0 ? data : null;
        _env.PluginsTxt = order.Length > 0 ? order : null;
        _env.SavesFolder = TxtSaves.Text.Trim() is { Length: > 0 } s ? s : null;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
