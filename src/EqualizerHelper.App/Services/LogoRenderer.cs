using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Draws the push-pin locomotive logo (Assets/logo.svg, a 64-unit canvas)
/// in a theme's colours, so the window, taskbar and tray icons can follow
/// the selected theme. The exe's own icon (Assets/app.ico) is fixed at
/// build time and always uses the default Locomotive colours.
/// </summary>
public static class LogoRenderer
{
    private static readonly int[] IconSizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

    /// <summary>A crisp bitmap of the logo, e.g. for the title bar.</summary>
    public static BitmapSource Render(AppTheme theme, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 64.0, size / 64.0));
            Draw(dc, theme, size);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// The logo as a multi-size .ico stream, so Windows can pick a sharp
    /// frame for each place it shows the icon (title bar, taskbar, tray).
    /// </summary>
    public static MemoryStream RenderIco(AppTheme theme)
    {
        var frames = new List<byte[]>();
        foreach (int size in IconSizes)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Render(theme, size)));
            using var png = new MemoryStream();
            encoder.Save(png);
            frames.Add(png.ToArray());
        }

        var ico = new MemoryStream();
        using (var w = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)IconSizes.Length);
            int offset = 6 + 16 * IconSizes.Length;
            for (int i = 0; i < IconSizes.Length; i++)
            {
                byte dimension = (byte)(IconSizes[i] >= 256 ? 0 : IconSizes[i]);
                w.Write(dimension);
                w.Write(dimension);
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(frames[i].Length);
                w.Write(offset);
                offset += frames[i].Length;
            }

            foreach (var frame in frames)
            {
                w.Write(frame);
            }
        }

        ico.Position = 0;
        return ico;
    }

    private static void Draw(DrawingContext dc, AppTheme theme, int pixelSize)
    {
        var dark = Frozen(new SolidColorBrush(theme.LogoDark));
        var light = Frozen(new SolidColorBrush(theme.LogoLight));
        var white = Brushes.White;

        // Background tile: gradient from bottom-left to top-right.
        var background = Frozen(new LinearGradientBrush(theme.LogoFrom, theme.LogoTo, new Point(0, 1), new Point(1, 0)));
        dc.DrawRoundedRectangle(background, null, new Rect(0, 0, 64, 64), 15, 15);

        // Boiler, cab and cab window.
        dc.DrawRoundedRectangle(dark, null, new Rect(10, 31, 30, 13), 3, 3);
        dc.DrawRoundedRectangle(dark, null, new Rect(32, 22, 14, 22), 3, 3);
        dc.DrawRoundedRectangle(light, null, new Rect(35, 25, 8, 7), 1.5, 1.5);

        // Push-pin chimney. At tiny sizes the needle is thicker so the pin
        // doesn't read as a lightbulb.
        dc.DrawEllipse(white, null, new Point(18, 20), 6, 6);
        dc.DrawRoundedRectangle(white, null, new Rect(15, 24, 6, 4), 1, 1);
        double needle = pixelSize <= 24 ? 3 : 2;
        dc.DrawLine(Frozen(new Pen(dark, needle)), new Point(18, 28), new Point(18, 31));

        // Equalizer-bar smoke.
        dc.DrawRoundedRectangle(white, null, new Rect(27, 12, 3, 6), 1.5, 1.5);
        dc.DrawRoundedRectangle(white, null, new Rect(32, 8, 3, 10), 1.5, 1.5);
        dc.DrawRoundedRectangle(white, null, new Rect(37, 11, 3, 7), 1.5, 1.5);

        // Wheels and track.
        var rim = Frozen(new Pen(light, 2));
        dc.DrawEllipse(dark, rim, new Point(18, 47), 4.5, 4.5);
        dc.DrawEllipse(dark, rim, new Point(36, 47), 4.5, 4.5);
        var track = Frozen(new Pen(dark, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        dc.DrawLine(track, new Point(8, 53), new Point(56, 53));
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
