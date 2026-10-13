using System.Runtime.InteropServices;

namespace VolumeKeeper;

internal sealed class AudioService : IDisposable
{
    private static readonly Guid OwnVolumeEvent = new("0275F908-FC47-4EBB-A458-BBA61CCAE56E");
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly DeviceNotification _deviceNotification;
    private IAudioEndpointVolume? _volume;
    private VolumeNotification? _volumeNotification;
    private bool _disposed;

    public DeviceInfo? Current { get; private set; }
    public event Action? DeviceChanged;
    public event Action<string, float, bool, bool>? VolumeChanged;

    public AudioService()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _deviceNotification = new DeviceNotification(() => DeviceChanged?.Invoke());
        try { _enumerator.RegisterEndpointNotificationCallback(_deviceNotification); }
        catch
        {
            Marshal.ReleaseComObject(_enumerator);
            throw;
        }
    }

    // Returns true only when the default multimedia endpoint itself has changed.
    public bool RefreshDefault()
    {
        IMMDevice? device = null;
        try
        {
            _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out device);
            device.GetId(out var idPointer);
            var endpointId = Marshal.PtrToStringUni(idPointer) ?? "";
            Marshal.FreeCoTaskMem(idPointer);

            if (string.Equals(Current?.EndpointId, endpointId, StringComparison.Ordinal))
            {
                ReadCurrentVolume();
                return false;
            }

            Unbind();
            var name = endpointId;
            string? stableId = null;
            try
            {
                device.OpenPropertyStore(0, out var properties);
                try
                {
                    name = ReadStringProperty(properties, PropertyKey.FriendlyName) ?? endpointId;
                    stableId = ReadStringProperty(properties, PropertyKey.StableId);
                }
                finally { Marshal.ReleaseComObject(properties); }
            }
            catch (COMException)
            {
                // A readable endpoint is still usable even if its metadata is unavailable.
            }

            var interfaceId = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref interfaceId, 23, IntPtr.Zero, out var volumeObject);
            _volume = (IAudioEndpointVolume)volumeObject;
            _volumeNotification = new VolumeNotification(endpointId,
                (id, level, muted, ours) => VolumeChanged?.Invoke(id, level, muted, ours));
            _volume.RegisterControlChangeNotify(_volumeNotification);
            Current = new DeviceInfo
            {
                EndpointId = endpointId,
                StableId = stableId,
                Name = name
            };
            ReadCurrentVolume();
            return true;
        }
        catch (COMException error) when ((uint)error.HResult == 0x80070490)
        {
            var changed = Current is not null;
            Unbind();
            return changed;
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
        }
    }

    public void ReadCurrentVolume()
    {
        if (_volume is null || Current is null) return;
        _volume.GetMasterVolumeLevelScalar(out var level);
        _volume.GetMute(out var muted);
        Current.Volume = level;
        Current.Muted = muted;
    }

    public void SetVolume(float value)
    {
        if (_volume is null || Current is null) throw new InvalidOperationException("出力先がありません。");
        var eventId = OwnVolumeEvent;
        _volume.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), ref eventId);
        ReadCurrentVolume();
    }

    private static string? ReadStringProperty(IPropertyStore properties, PropertyKey key)
    {
        try
        {
            properties.GetValue(ref key, out var value);
            try
            {
                return value.VariantType == 31 && value.Pointer != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.Pointer)
                    : null;
            }
            finally { PropVariantClear(ref value); }
        }
        catch (COMException)
        {
            return null;
        }
    }

    private void Unbind()
    {
        if (_volume is not null)
        {
            if (_volumeNotification is not null)
            {
                try { _volume.UnregisterControlChangeNotify(_volumeNotification); }
                catch (COMException) { }
            }
            Marshal.ReleaseComObject(_volume);
        }
        _volume = null;
        _volumeNotification = null;
        Current = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unbind();
        try { _enumerator.UnregisterEndpointNotificationCallback(_deviceNotification); }
        catch (COMException) { }
        Marshal.ReleaseComObject(_enumerator);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant variant);

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DeviceNotification(Action changed) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint state) { changed(); return 0; }
        public int OnDeviceAdded(string deviceId) { changed(); return 0; }
        public int OnDeviceRemoved(string deviceId) { changed(); return 0; }
        public int OnDefaultDeviceChanged(DataFlow flow, Role role, string? deviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia) changed();
            return 0;
        }
        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class VolumeNotification(string endpointId,
        Action<string, float, bool, bool> changed) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notification)
        {
            if (notification == IntPtr.Zero) return 0;
            var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notification);
            changed(endpointId, data.MasterVolume, data.Muted != 0, data.EventContext == OwnVolumeEvent);
            return 0;
        }
    }
}

internal enum DataFlow { Render, Capture, All }
internal enum Role { Console, Multimedia, Communications }

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, uint propertyId)
{
    public Guid FormatId = formatId;
    public uint PropertyId = propertyId;

    public static PropertyKey FriendlyName => new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    public static PropertyKey StableId => new(
        new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 12);
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort VariantType;
    [FieldOffset(8)] public IntPtr Pointer;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioVolumeNotificationData
{
    public Guid EventContext;
    public int Muted;
    public float MasterVolume;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(DataFlow flow, uint stateMask, out IntPtr devices);
    void GetDefaultAudioEndpoint(DataFlow flow, Role role, out IMMDevice device);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint state);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    [PreserveSig] int OnDefaultDeviceChanged(DataFlow flow, Role role,
        [MarshalAs(UnmanagedType.LPWStr)] string? deviceId);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        PropertyKey key);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid interfaceId, uint classContext, IntPtr activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object interfaceObject);
    void OpenPropertyStore(uint access, out IPropertyStore properties);
    void GetId(out IntPtr id);
    void GetState(out uint state);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void Commit();
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    void GetChannelCount(out uint count);
    void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
    void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    void GetMasterVolumeLevel(out float levelDb);
    void GetMasterVolumeLevelScalar(out float level);
    void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
    void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    void GetChannelVolumeLevel(uint channel, out float levelDb);
    void GetChannelVolumeLevelScalar(uint channel, out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    void GetVolumeStepInfo(out uint step, out uint stepCount);
    void VolumeStepUp(ref Guid eventContext);
    void VolumeStepDown(ref Guid eventContext);
    void QueryHardwareSupport(out uint mask);
    void GetVolumeRange(out float minimumDb, out float maximumDb, out float incrementDb);
}

[Guid("657804FA-D6AD-4496-8A60-352752AF4F89"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr notification);
}
