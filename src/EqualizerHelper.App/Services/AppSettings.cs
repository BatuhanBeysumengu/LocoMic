namespace EqualizerHelper.App.Services;

public class AppSettings
{
    public string? CaptureDeviceId { get; set; }
    public string? RenderDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }
    public bool MonitorEnabled { get; set; } = true;
    public string? PresetName { get; set; }
}
