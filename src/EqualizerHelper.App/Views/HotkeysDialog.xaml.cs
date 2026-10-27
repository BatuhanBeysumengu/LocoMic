using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EqualizerHelper.App.Services;

namespace EqualizerHelper.App.Views;

public partial class HotkeysDialog : Window
{
    private readonly Dictionary<HotkeyAction, HotkeyGesture?> _hotkeys;
    private readonly Dictionary<HotkeyAction, System.Windows.Controls.TextBox> _boxes = new();

    public Dictionary<HotkeyAction, HotkeyGesture?> Result => new(_hotkeys);

    public HotkeysDialog(IReadOnlyDictionary<HotkeyAction, HotkeyGesture?> current)
    {
        InitializeComponent();
        DarkTitleBar.ApplyWhenReady(this);
        Icon = ThemeService.WindowIcon;
        _hotkeys = current.ToDictionary(kv => kv.Key, kv => kv.Value);

        var first = new HotkeyGesture(HotkeyGesture.PresetNumberModifiers, Key.D1);
        var last = new HotkeyGesture(HotkeyGesture.PresetNumberModifiers, Key.D9);
        PresetNumbersText.Text = $"Also always available: {first} … {last} jumps straight to preset 1–9 in the list.";

        foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>())
        {
            AddRow(action);
        }
    }

    private void AddRow(HotkeyAction action)
    {
        int row = RowsGrid.RowDefinitions.Count;
        RowsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = HotkeyGesture.DisplayName(action),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4),
        };
        Grid.SetRow(label, row);
        RowsGrid.Children.Add(label);

        var box = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            Margin = new Thickness(0, 4, 8, 4),
            ToolTip = "Click here, then press a key combination or a side mouse button (Mouse4/Mouse5)",
        };
        box.PreviewKeyDown += (_, e) => OnBoxKeyDown(action, e);
        box.PreviewMouseDown += (_, e) => OnBoxMouseDown(action, e);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        RowsGrid.Children.Add(box);
        _boxes[action] = box;

        var clear = new System.Windows.Controls.Button { Content = "Clear", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 4, 0, 4) };
        clear.Click += (_, _) =>
        {
            _hotkeys[action] = null;
            Refresh(action);
        };
        Grid.SetRow(clear, row);
        Grid.SetColumn(clear, 2);
        RowsGrid.Children.Add(clear);

        Refresh(action);
    }

    private void OnBoxMouseDown(HotkeyAction action, MouseButtonEventArgs e)
    {
        // Left click just selects the box; only the side buttons can be hotkeys.
        if (!HotkeyGesture.IsAllowedMouseButton(e.ChangedButton))
        {
            return;
        }

        e.Handled = true;
        HideError();
        _hotkeys[action] = HotkeyGesture.ForMouse(Keyboard.Modifiers, e.ChangedButton);
        Refresh(action);
    }

    private void OnBoxKeyDown(HotkeyAction action, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;

        // Alt combinations arrive as Key.System.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Tab)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && !HotkeyGesture.IsAllowedWithoutModifiers(key))
        {
            ShowError("Use at least one of Ctrl, Alt, Shift or Win (numpad keys can be used on their own), so normal typing isn't captured.");
            return;
        }

        HideError();
        _hotkeys[action] = new HotkeyGesture(modifiers, key);
        Refresh(action);
    }

    private void Refresh(HotkeyAction action) =>
        _boxes[action].Text = _hotkeys[action]?.ToString() ?? "(none)";

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (action, gesture) in HotkeyGesture.Defaults)
        {
            _hotkeys[action] = gesture;
            Refresh(action);
        }

        HideError();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var duplicate = _hotkeys.Values
            .Where(g => g != null)
            .GroupBy(g => g)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            ShowError($"{duplicate.Key} is used for more than one action.");
            return;
        }

        var presetNumberClash = _hotkeys.Values.FirstOrDefault(g =>
            g != null && g.Modifiers == HotkeyGesture.PresetNumberModifiers && g.Key is >= Key.D1 and <= Key.D9);
        if (presetNumberClash != null)
        {
            ShowError($"{presetNumberClash} is already used to jump to a preset by number.");
            return;
        }

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorTextBlock.Visibility = Visibility.Collapsed;
}
