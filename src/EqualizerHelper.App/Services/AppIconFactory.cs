using System.Drawing;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Draws the app's equalizer-bars glyph in code (no binary .ico asset
/// needed), shared by the tray icon and the window's own titlebar/taskbar icon.
/// </summary>
public static class AppIconFactory
{
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 45, 212, 167)); // #2DD4A7 accent teal
            int[] heights = { 12, 22, 16, 26 };
            int x = 3;
            foreach (int h in heights)
            {
                g.FillRectangle(brush, x, 30 - h, 5, h);
                x += 7;
            }
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
