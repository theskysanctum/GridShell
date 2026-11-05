using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace GridShell.Core.Interop.Audio;

/// <summary>Windows has no public API to CHANGE the default audio device — only to query it and
/// control volume (that's all IMMDeviceEnumerator/IAudioEndpointVolume cover). Setting the
/// default requires this undocumented but widely-used-and-stable COM interface (the same one
/// NirSoft's SoundVolumeView, EarTrumpet, and other volume-control tools use), reverse-engineered
/// years ago and unchanged since.</summary>
[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPolicyConfig
{
    void GetMixFormat(string pszDeviceName, IntPtr ppFormat);
    void GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);
    void ResetDeviceFormat(string pszDeviceName);
    void SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
    void GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
    void SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
    void GetShareMode(string pszDeviceName, IntPtr pMode);
    void SetShareMode(string pszDeviceName, IntPtr mode);
    void GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
    void SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
    void SetDefaultEndpoint(string pszDeviceName, CoreAudio.ERole role);
    void SetEndpointVisibility(string pszDeviceName, bool bVisible);
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
public class CPolicyConfigClient
{
}
