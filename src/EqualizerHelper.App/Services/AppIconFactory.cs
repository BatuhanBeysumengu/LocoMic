using System;
using System.Drawing;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Loads the app logo (Assets/app.ico, the "push-pin locomotive") as a
/// Windows Forms icon for the system tray, at the small-icon size Windows
/// asks for.
/// </summary>
public static class AppIconFactory
{
    public static Icon CreateIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/LocoMic;component/Assets/app.ico"))
            ?? throw new InvalidOperationException("The app icon resource is missing.");
        using var stream = resource.Stream;
        return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }
}
