using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace EqualizerHelper.App.Services;

/// <summary>One colour theme: the palette behind every brush in Themes/DarkStudio.xaml.</summary>
public record AppTheme(
    string Name,
    string Description,
    Color WindowBackground,
    Color Panel,
    Color PanelBorder,
    Color ControlBackground,
    Color ControlBorder,
    Color Accent,
    Color AccentPressed,
    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,
    Color LogoFrom,
    Color LogoTo)
{
    // The locomotive itself looks the same in every theme; only the logo's
    // tile gradient (LogoFrom bottom-left to LogoTo top-right) changes.
    public Color LogoDark => Color.FromRgb(0x2A, 0x0F, 0x14);
    public Color LogoLight => Color.FromRgb(0xFF, 0xE3, 0xC4);

    // For the theme picker swatches.
    public SolidColorBrush AccentBrush => new(Accent);
    public SolidColorBrush PanelBrush => new(Panel);
}

/// <summary>
/// Switches the app's colours at runtime. The UI references every palette
/// brush with DynamicResource, so replacing the brushes in the application
/// resources re-colours all open windows immediately.
/// </summary>
public static class ThemeService
{
    public const string DefaultThemeName = "Locomotive";

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static IReadOnlyList<AppTheme> Themes { get; } = new[]
    {
        // Matches the push-pin locomotive logo: warm orange on near-black.
        new AppTheme("Locomotive", "Orange on black, like the logo",
            C("#121012"), C("#1C1717"), C("#2E2626"), C("#241E1E"), C("#3A3030"),
            C("#FF8A4C"), C("#E9733A"), C("#F2EBE6"), C("#B3A7A0"), C("#7D716B"),
            C("#FF5F6D"), C("#FFC371")),
        new AppTheme("Mint", "The original green",
            C("#121315"), C("#1B1D20"), C("#2A2D31"), C("#202327"), C("#34383D"),
            C("#2DD4A7"), C("#22B890"), C("#E8E9EA"), C("#9AA0A6"), C("#6B7076"),
            C("#11998E"), C("#5EEAD4")),
        new AppTheme("Ocean", "Cool blue",
            C("#0F1318"), C("#161B22"), C("#263040"), C("#1B222C"), C("#2E3A4A"),
            C("#38BDF8"), C("#0EA5E9"), C("#E6EDF3"), C("#9AA7B4"), C("#6B7785"),
            C("#2563EB"), C("#67E8F9")),
        new AppTheme("Grape", "Soft purple",
            C("#131118"), C("#1B1722"), C("#2C2638"), C("#221D2B"), C("#382F47"),
            C("#A78BFA"), C("#8B5CF6"), C("#ECE8F3"), C("#A79FB6"), C("#766E85"),
            C("#7C3AED"), C("#D8B4FE")),
        new AppTheme("Cherry", "Bold red",
            C("#140F11"), C("#1D1518"), C("#33242A"), C("#251B1F"), C("#3D2A31"),
            C("#F43F5E"), C("#E11D48"), C("#F3E8EA"), C("#B8A3A8"), C("#80696F"),
            C("#BE123C"), C("#FB7185")),
    };

    public static AppTheme Current { get; private set; } = Themes[0];

    /// <summary>The logo in the current theme's colours, as a multi-size window icon.</summary>
    public static ImageSource WindowIcon { get; private set; } = null!;

    /// <summary>A sharp 64 px logo in the current theme's colours (title bar).</summary>
    public static ImageSource HeaderLogo { get; private set; } = null!;

    /// <summary>Raised after a theme is applied, so icons outside WPF (the tray) can follow.</summary>
    public static event System.EventHandler? ThemeChanged;

    /// <summary>The current logo as a tray icon at the small-icon size Windows asks for.</summary>
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var ico = LogoRenderer.RenderIco(Current);
        return new System.Drawing.Icon(ico, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    public static AppTheme Find(string? name) =>
        Themes.FirstOrDefault(t => t.Name == name) ?? Themes.First(t => t.Name == DefaultThemeName);

    public static void Apply(AppTheme theme)
    {
        var resources = System.Windows.Application.Current.Resources;
        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        Set("WindowBackgroundBrush", theme.WindowBackground);
        Set("PanelBrush", theme.Panel);
        Set("PanelBorderBrush", theme.PanelBorder);
        Set("ControlBackgroundBrush", theme.ControlBackground);
        Set("ControlBorderBrush", theme.ControlBorder);
        Set("AccentBrush", theme.Accent);
        Set("AccentPressedBrush", theme.AccentPressed);
        Set("TextPrimaryBrush", theme.TextPrimary);
        Set("TextSecondaryBrush", theme.TextSecondary);
        Set("TextMutedBrush", theme.TextMuted);
        Set("SliderTrackBrush", theme.PanelBorder);

        Current = theme;

        using (var ico = LogoRenderer.RenderIco(theme))
        {
            var icon = BitmapFrame.Create(ico, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            icon.Freeze();
            WindowIcon = icon;
        }

        HeaderLogo = LogoRenderer.Render(theme, 64);

        // Window frames (drawn by Windows) and icons follow the theme too.
        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
        {
            DarkTitleBar.Apply(window);
            window.Icon = WindowIcon;
        }

        ThemeChanged?.Invoke(null, System.EventArgs.Empty);
    }
}
