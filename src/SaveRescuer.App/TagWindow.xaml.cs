using System.Windows;
using System.Windows.Controls;
using SaveRescuer.Core;

namespace SaveRescuer.App;

public partial class TagWindow : Window
{
    private readonly string _currentName;

    public string Prefix => PrefixBox.Text;

    public TagWindow(string currentName, string suggestedPrefix)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _currentName = currentName;
        PrefixBox.Text = suggestedPrefix;
        UpdatePreview();
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string preset }) PrefixBox.Text = preset;
    }

    private void OnPrefixChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (Preview is null) return;
        var name = SaveSurgeon.ApplyPrefix(_currentName, new SurgeryOptions { Prefix = PrefixBox.Text });
        Preview.Text = $"load menu will read:  {name}";
    }

    private void OnWriteClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
