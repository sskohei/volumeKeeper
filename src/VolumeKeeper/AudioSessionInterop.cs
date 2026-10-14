using System.Runtime.InteropServices;

namespace VolumeKeeper;

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(ref Guid sessionGuid, uint flags, out IntPtr control);
    void GetSimpleAudioVolume(ref Guid sessionGuid, uint flags, out IntPtr volume);
    void GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
    void RegisterSessionNotification(IntPtr callback);
    void UnregisterSessionNotification(IntPtr callback);
    void RegisterDuckNotification(IntPtr sessionId, IntPtr callback);
    void UnregisterDuckNotification(IntPtr callback);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int count);
    void GetSession(int index, out IAudioSessionControl control);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    void GetState(out int state);
    void GetDisplayName(out IntPtr name);
    void SetDisplayName(IntPtr name, ref Guid eventContext);
    void GetIconPath(out IntPtr path);
    void SetIconPath(IntPtr path, ref Guid eventContext);
    void GetGroupingParam(out Guid grouping);
    void SetGroupingParam(ref Guid grouping, ref Guid eventContext);
    void RegisterAudioSessionNotification(IntPtr callback);
    void UnregisterAudioSessionNotification(IntPtr callback);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2 : IAudioSessionControl
{
    // Classic [ComImport] ignores C# interface inheritance when laying out the
    // COM vtable. Repeat the nine base slots before the five added methods.
    new void GetState(out int state);
    new void GetDisplayName(out IntPtr name);
    new void SetDisplayName(IntPtr name, ref Guid eventContext);
    new void GetIconPath(out IntPtr path);
    new void SetIconPath(IntPtr path, ref Guid eventContext);
    new void GetGroupingParam(out Guid grouping);
    new void SetGroupingParam(ref Guid grouping, ref Guid eventContext);
    new void RegisterAudioSessionNotification(IntPtr callback);
    new void UnregisterAudioSessionNotification(IntPtr callback);
    void GetSessionIdentifier(out IntPtr identifier);
    void GetSessionInstanceIdentifier(out IntPtr identifier);
    [PreserveSig] int GetProcessId(out uint processId);
    [PreserveSig] int IsSystemSoundsSession();
    void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    void GetPeakValue(out float peak);
    void GetMeteringChannelCount(out uint count);
    void GetChannelsPeakValues(uint count, IntPtr peaks);
    void QueryHardwareSupport(out uint mask);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid eventContext);
    void GetMasterVolume(out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
}
