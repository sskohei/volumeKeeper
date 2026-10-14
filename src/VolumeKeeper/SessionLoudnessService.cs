using System.Runtime.InteropServices;

namespace VolumeKeeper;

// Audio sessions expose a peak meter and an independent volume control. The
// meter is before session attenuation, so one target peak can be used for all
// applications without changing the output device's master volume.
internal sealed class SessionLoudnessService : IDisposable
{
    private static readonly Guid OwnEvent = new("04BA0DC4-05D9-440A-BD6F-7FB36C9864E2");
    private readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
    private readonly Dictionary<string, SessionLevel> _levels = new(StringComparer.Ordinal);
    private IAudioSessionManager2? _manager;
    private string? _endpointId;

    public void Tick(string endpointId)
    {
        if (!string.Equals(_endpointId, endpointId, StringComparison.Ordinal))
        {
            Unbind();
            Bind(endpointId);
        }

        _manager!.GetSessionEnumerator(out var sessions);
        try
        {
            sessions.GetCount(out var count);
            var present = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                sessions.GetSession(index, out var session);
                try
                {
                    try { ProcessSession(session, present); }
                    catch (COMException) { } // A session can end while it is enumerated.
                }
                finally { Marshal.ReleaseComObject(session); }
            }

            foreach (var key in _levels.Keys.Where(key => !present.Contains(key)).ToArray())
                _levels.Remove(key);
        }
        finally { Marshal.ReleaseComObject(sessions); }
    }

    private void Bind(string endpointId)
    {
        _enumerator.GetDevice(endpointId, out var device);
        try
        {
            var id = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref id, 23, IntPtr.Zero, out var manager);
            _manager = (IAudioSessionManager2)manager;
            _endpointId = endpointId;
        }
        finally { Marshal.ReleaseComObject(device); }
    }

    private void ProcessSession(IAudioSessionControl session, HashSet<string> present)
    {
        if (session is not IAudioSessionControl2 info ||
            session is not IAudioMeterInformation meter ||
            session is not ISimpleAudioVolume volume) return;

        if (info.IsSystemSoundsSession() == 0 ||
            info.GetProcessId(out var processId) < 0 || processId == 0)
            return; // Leave Windows system sounds and unidentified sessions alone.
        info.GetSessionInstanceIdentifier(out var idPointer);
        var id = Marshal.PtrToStringUni(idPointer);
        Marshal.FreeCoTaskMem(idPointer);
        if (string.IsNullOrEmpty(id)) return;
        present.Add(id);

        session.GetState(out var state);
        if (state == 2)
        {
            _levels.Remove(id);
            return;
        }
        volume.GetMute(out var muted);
        volume.GetMasterVolume(out var actual);
        if (!_levels.TryGetValue(id, out var level))
        {
            level = new SessionLevel(actual);
            _levels.Add(id, level);
        }
        else if (Math.Abs(actual - level.Applied) > 0.005f)
        {
            // The user or another application changed the session volume.
            level.Baseline = actual;
            level.Applied = actual;
        }

        if (!muted && !level.Prepared)
        {
            // Prime even inactive sessions so their first sound is attenuated.
            var initial = LoudnessPolicy.InitialVolume(level.Baseline);
            if (Math.Abs(initial - actual) >= 0.005f)
            {
                var initialEvent = OwnEvent;
                volume.SetMasterVolume(initial, ref initialEvent);
                level.Applied = initial;
                actual = initial;
            }
            level.Prepared = true;
        }

        // Keep the adjusted level while playback is paused. Restoring here made
        // the next play start at full volume until the following meter tick.
        if (state != 1) return;
        if (muted || actual <= 0f) return;
        meter.GetPeakValue(out var peak);
        level.Peak = LoudnessPolicy.NextPeak(level.Peak, peak);
        var next = LoudnessPolicy.NextVolume(level.Baseline, actual, level.Peak, peak);
        if (Math.Abs(next - actual) < 0.005f) return;
        var eventId = OwnEvent;
        volume.SetMasterVolume(next, ref eventId);
        level.Applied = next;
    }

    private void Restore()
    {
        if (_manager is null) return;
        try
        {
            _manager.GetSessionEnumerator(out var sessions);
            try
            {
                sessions.GetCount(out var count);
                for (var index = 0; index < count; index++)
                {
                    sessions.GetSession(index, out var session);
                    try
                    {
                        try
                        {
                            if (session is not IAudioSessionControl2 info ||
                                session is not ISimpleAudioVolume volume) continue;
                            info.GetSessionInstanceIdentifier(out var pointer);
                            var id = Marshal.PtrToStringUni(pointer);
                            Marshal.FreeCoTaskMem(pointer);
                            if (id is null || !_levels.TryGetValue(id, out var level)) continue;
                            volume.GetMasterVolume(out var actual);
                            if (Math.Abs(actual - level.Applied) > 0.005f) continue;
                            var eventId = OwnEvent;
                            volume.SetMasterVolume(level.Baseline, ref eventId);
                        }
                        catch (COMException) { }
                    }
                    finally { Marshal.ReleaseComObject(session); }
                }
            }
            finally { Marshal.ReleaseComObject(sessions); }
        }
        catch (COMException) { }
    }

    private void Unbind()
    {
        Restore();
        if (_manager is not null) Marshal.ReleaseComObject(_manager);
        _manager = null;
        _endpointId = null;
        _levels.Clear();
    }

    public void Dispose()
    {
        Unbind();
        Marshal.ReleaseComObject(_enumerator);
    }

    private sealed class SessionLevel(float baseline)
    {
        public float Baseline = baseline;
        public float Applied = baseline;
        public float Peak;
        public bool Prepared;
    }
}
