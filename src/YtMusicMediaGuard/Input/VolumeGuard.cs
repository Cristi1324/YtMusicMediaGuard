using System.Runtime.InteropServices;

namespace YtMusicMediaGuard;

internal static class VolumeGuard
{
    private static readonly object Gate = new();
    private static Snapshot? _stable;
    private static DateTime _holdUntilUtc;
    private static System.Threading.Timer? _timer;

    public static void Start()
    {
        if (TryRead(out var snapshot)) _stable = snapshot;
        _timer = new System.Threading.Timer(_ => Refresh(), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public static void NoteMediaKey()
    {
        lock (Gate) _holdUntilUtc = DateTime.UtcNow.AddMilliseconds(280);
        _ = Task.Run(async () =>
        {
            await Task.Delay(80);
            Restore();
            await Task.Delay(120);
            Restore();
        });
    }

    public static bool TryReadLevel(out float level, out bool mute)
    {
        if (!TryRead(out var snapshot))
        {
            level = 0;
            mute = false;
            return false;
        }

        level = snapshot.Level;
        mute = snapshot.Mute;
        return true;
    }

    public static void Stop() => _timer?.Dispose();

    private static void Refresh()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow < _holdUntilUtc) return;
        }

        if (!TryRead(out var snapshot)) return;
        lock (Gate)
        {
            if (DateTime.UtcNow < _holdUntilUtc) return;
            _stable = snapshot;
        }
    }

    private static void Restore()
    {
        Snapshot? target;
        lock (Gate) target = _stable;
        if (target is null || !TryRead(out var current)) return;
        if (Math.Abs(current.Level - target.Value.Level) < 0.001f && current.Mute == target.Value.Mute) return;
        if (TryWrite(target.Value))
            Logger.Info("Restored Windows volume after a captured media key.");
    }

    private static bool TryRead(out Snapshot snapshot)
    {
        snapshot = default;
        try
        {
            var endpoint = OpenEndpoint();
            if (endpoint is null) return false;
            if (endpoint.GetMasterVolumeLevelScalar(out var level) != 0) return false;
            if (endpoint.GetMute(out var mute) != 0) return false;
            snapshot = new Snapshot(level, mute);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Volume read failed: " + ex.Message);
            return false;
        }
    }

    private static bool TryWrite(Snapshot snapshot)
    {
        try
        {
            var endpoint = OpenEndpoint();
            if (endpoint is null) return false;
            var context = Guid.Empty;
            var volume = endpoint.SetMasterVolumeLevelScalar(snapshot.Level, ref context);
            var mute = endpoint.SetMute(snapshot.Mute, ref context);
            return volume == 0 && mute == 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Volume restore failed: " + ex.Message);
            return false;
        }
    }

    private static IAudioEndpointVolume? OpenEndpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (enumerator.GetDefaultAudioEndpoint(0, 0, out var device) != 0 || device is null) return null;
        var iid = typeof(IAudioEndpointVolume).GUID;
        if (device.Activate(ref iid, 23, IntPtr.Zero, out var endpoint) != 0) return null;
        return endpoint;
    }

    private readonly record struct Snapshot(float Level, bool Mute);

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int classContext, IntPtr activationParams, out IAudioEndpointVolume endpoint);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint channelCount);
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
