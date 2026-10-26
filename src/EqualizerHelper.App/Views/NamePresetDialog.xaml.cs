using System.Windows;

namespace EqualizerHelper.App.Views;

public partial class NamePresetDialog : Window
{
    public string PresetName { get; private set; } = string.Empty;

    public NamePresetDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        PresetName = name;
        DialogResult = true;
    }
}
