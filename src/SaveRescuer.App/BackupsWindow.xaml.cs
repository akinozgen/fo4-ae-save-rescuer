using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SaveRescuer.Core;

namespace SaveRescuer.App;

public sealed record BackupRow(VaultEntry Entry)
{
    public string When => Entry.CreatedAt.ToString("yyyy-MM-dd HH:mm");
    public string Character => string.IsNullOrWhiteSpace(Entry.PlayerName) ? "(unnamed)" : Entry.PlayerName;
    public string Level => Entry.Level > 0 ? Entry.Level.ToString() : "-";
    public string InGameName => Entry.Location;
    public string Size => $"{Entry.MegaBytes:N1} MB";
    public string Reason => Entry.Reason;
    public string OriginalName => Entry.OriginalName;
}

public partial class BackupsWindow : Window
{
    private readonly SaveVault _vault;
    private readonly AppSettings _settings;

    public BackupsWindow(SaveVault vault, AppSettings settings)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _vault = vault;
        _settings = settings;
        Reload();
    }

    private BackupRow? Current => Grid.SelectedItem as BackupRow;

    private void Reload()
    {
        var rows = _vault.Entries().Select(e => new BackupRow(e)).ToList();
        Grid.ItemsSource = rows;
        var total = rows.Sum(r => r.Entry.Bytes) / 1024.0 / 1024.0;
        StatusLabel.Text = rows.Count == 0
            ? "No copies kept yet. One is made automatically before each rescue."
            : $"{rows.Count} copies, {total:N0} MB in {_vault.Root}";
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;

        var target = Path.Combine(Path.GetDirectoryName(row.Entry.OriginalPath) ?? "", row.Entry.OriginalName);
        var overwrite = false;
        if (File.Exists(target))
        {
            var answer = MessageBox.Show(
                $"{row.Entry.OriginalName} already exists where this copy came from.\n\n" +
                "Yes - overwrite it\nNo - keep both, restore under a free name",
                "Restore", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            overwrite = answer == MessageBoxResult.Yes;
        }

        Restore(row, null, overwrite);
    }

    private void OnRestoreElsewhereClick(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;

        var picker = new OpenFolderDialog
        {
            Title = "Restore into",
            InitialDirectory = _settings.SavesFolder ?? ""
        };
        if (picker.ShowDialog(this) == true) Restore(row, picker.FolderName, overwrite: false);
    }

    private void Restore(BackupRow row, string? folder, bool overwrite)
    {
        try
        {
            var path = _vault.Restore(row.Entry, folder, overwrite);
            StatusLabel.Text = $"Restored to {path}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not restore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;

        var answer = MessageBox.Show(
            $"Delete the kept copy of {row.Entry.OriginalName} ({row.Size})?\n\n" +
            "This removes the backup, not the save in your game folder.",
            "Delete backup", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        _vault.Forget(row.Entry);
        Reload();
    }

    private void OnPruneClick(object sender, RoutedEventArgs e)
    {
        var dropped = _vault.Prune();
        Reload();
        StatusLabel.Text = dropped == 0 ? "Every entry still has its file." : $"{dropped} stale entries removed.";
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_vault.Root);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_vault.Root}\"") { UseShellExecute = true });
    }
}
