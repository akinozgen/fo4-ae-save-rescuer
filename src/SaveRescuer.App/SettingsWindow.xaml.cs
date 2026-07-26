using System.IO;
using System.Windows;
using Microsoft.Win32;
using SaveRescuer.Core;

namespace SaveRescuer.App;

public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Result = current;
        Show(current);
    }

    private void Show(AppSettings settings)
    {
        DataBox.Text = settings.DataFolder ?? "";
        SavesBox.Text = settings.SavesFolder ?? "";
        VaultBox.Text = settings.VaultFolder ?? SaveVault.DefaultRoot;
        PrefixBox.Text = settings.Prefix;
        SlotBox.Text = settings.SlotStart.ToString();
        VanillaBox.IsChecked = settings.VanillaOnly;
        BackupBox.IsChecked = settings.BackupBeforeRescue;
        ReplaceTagBox.IsChecked = settings.ReplaceExistingTag;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(SavesBox.Text))
        {
            MessageBox.Show("That save folder does not exist.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new AppSettings
        {
            DataFolder = Blank(DataBox.Text),
            SavesFolder = SavesBox.Text.Trim(),
            VaultFolder = Blank(VaultBox.Text),
            Prefix = PrefixBox.Text,
            SlotStart = int.TryParse(SlotBox.Text, out var slot) && slot >= 0 ? slot : 900,
            VanillaOnly = VanillaBox.IsChecked == true,
            BackupBeforeRescue = BackupBox.IsChecked == true,
            ReplaceExistingTag = ReplaceTagBox.IsChecked == true
        };

        DialogResult = true;
        Close();
    }

    private void OnDetectClick(object sender, RoutedEventArgs e)
    {
        var detected = AppSettings.Detected();
        DataBox.Text = detected.DataFolder ?? "";
        SavesBox.Text = detected.SavesFolder ?? "";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnBrowseDataClick(object sender, RoutedEventArgs e) => Browse(DataBox, "Pick the game's Data folder");
    private void OnBrowseSavesClick(object sender, RoutedEventArgs e) => Browse(SavesBox, "Pick the save folder");
    private void OnBrowseVaultClick(object sender, RoutedEventArgs e) => Browse(VaultBox, "Pick the backup folder");

    private void Browse(System.Windows.Controls.TextBox box, string title)
    {
        var picker = new OpenFolderDialog { Title = title, InitialDirectory = box.Text };
        if (picker.ShowDialog(this) == true) box.Text = picker.FolderName;
    }

    private static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
