using System;
using System.Windows;
using EqualizerHelper.App.Services;

namespace EqualizerHelper.App.Views;

public partial class NamePresetDialog : Window
{
    private readonly Func<string, string?>? _validate;

    public string PresetName { get; private set; } = string.Empty;

    /// <param name="validate">Returns an error message to show for a name, or null if it's acceptable.</param>
    public NamePresetDialog(
        string? prompt = null,
        string initialName = "",
        string cancelText = "Don't Save",
        Func<string, string?>? validate = null)
    {
        InitializeComponent();
        DarkTitleBar.ApplyWhenReady(this);
        _validate = validate;

        if (prompt != null)
        {
            PromptTextBlock.Text = prompt;
        }

        CancelButton.Content = cancelText;
        NameTextBox.Text = initialName;

        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        string? error = _validate?.Invoke(name);
        if (error != null)
        {
            ErrorTextBlock.Text = error;
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        PresetName = name;
        DialogResult = true;
    }
}
