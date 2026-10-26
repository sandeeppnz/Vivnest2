using Vivnest.Core.Devices.Audio;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Audio;

public sealed class AudioClipSourceFactory : IAudioClipSourceFactory
{
    // Every audio-capable device is an RTSP camera today; branch on a
    // setting here if another source ever appears, as CameraFactory notes.
    public IAudioClipSource Create(DeviceOptions deviceOptions) =>
        new RtspAudioClipSource(deviceOptions);
}
