using Vanara.PInvoke;
using static Vanara.PInvoke.CoreAudio;

namespace GridShell.Core.Interop.Audio;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

/// <summary>Wraps the Core Audio APIs for output/input device listing, volume/mute, and
/// switching the default device. Windows has no public API for that last part — see
/// <see cref="IPolicyConfig"/>.</summary>
public static class AudioDeviceManager
{
    private static readonly Ole32.PROPERTYKEY FriendlyNameKey =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static List<AudioDeviceInfo> EnumerateDevices(EDataFlow dataFlow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        var defaultDevice = enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.eMultimedia)!;
        var defaultId = GetDeviceId(defaultDevice);

        var collection = enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE.DEVICE_STATE_ACTIVE)!;
        var count = collection.GetCount();

        var result = new List<AudioDeviceInfo>();
        for (uint i = 0; i < count; i++)
        {
            collection.Item(i, out var device);
            var id = GetDeviceId(device!);
            result.Add(new AudioDeviceInfo(id, GetFriendlyName(device!), id == defaultId));
        }

        return result;
    }

    public static void SetDefaultDevice(string deviceId)
    {
        var policyConfig = (IPolicyConfig)new CPolicyConfigClient();
        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
        {
            policyConfig.SetDefaultEndpoint(deviceId, role);
        }
    }

    public static float GetVolume(EDataFlow dataFlow) =>
        WithDefaultEndpointVolume(dataFlow, v => v.GetMasterVolumeLevelScalar());

    public static void SetVolume(EDataFlow dataFlow, float level) =>
        WithDefaultEndpointVolume(dataFlow, v =>
        {
            v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), in Guid.Empty);
            return 0;
        });

    public static bool GetMute(EDataFlow dataFlow) =>
        WithDefaultEndpointVolume(dataFlow, v => v.GetMute());

    public static void SetMute(EDataFlow dataFlow, bool mute) =>
        WithDefaultEndpointVolume(dataFlow, v =>
        {
            v.SetMute(mute, in Guid.Empty);
            return false;
        });

    private static T WithDefaultEndpointVolume<T>(EDataFlow dataFlow, Func<IAudioEndpointVolume, T> action)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.eMultimedia)!;
        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(in iid, Ole32.CLSCTX.CLSCTX_ALL, IntPtr.Zero, out var obj);
        return action((IAudioEndpointVolume)obj!);
    }

    private static string GetDeviceId(IMMDevice device) => device.GetId()!;

    private static string GetFriendlyName(IMMDevice device)
    {
        var store = device.OpenPropertyStore(Vanara.PInvoke.STGM.STGM_READ)!;
        return store.GetValue(in FriendlyNameKey)?.ToString() ?? "(unknown device)";
    }
}
