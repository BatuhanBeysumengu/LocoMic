using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;

namespace EqualizerHelper.Audio;

public static class DeviceManager
{
    public const string VirtualCableInputNameHint = "CABLE Input";

    public static List<AudioDeviceInfo> GetCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, d))
            .ToList();
    }

    public static List<AudioDeviceInfo> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, d))
            .ToList();
    }

    /// <summary>
    /// Finds VB-Audio Virtual Cable's render endpoint ("CABLE Input") among the
    /// given render devices, if the driver is installed.
    /// </summary>
    public static AudioDeviceInfo? FindVirtualCableInput(IEnumerable<AudioDeviceInfo> renderDevices)
    {
        return renderDevices.FirstOrDefault(d =>
            d.Name.Contains(VirtualCableInputNameHint, StringComparison.OrdinalIgnoreCase));
    }
}
