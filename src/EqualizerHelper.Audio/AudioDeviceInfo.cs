using NAudio.CoreAudioApi;

namespace EqualizerHelper.Audio;

public class AudioDeviceInfo
{
    public AudioDeviceInfo(string id, string name, MMDevice device)
    {
        Id = id;
        Name = name;
        Device = device;
    }

    public string Id { get; }
    public string Name { get; }
    public MMDevice Device { get; }

    public override string ToString() => Name;
}
