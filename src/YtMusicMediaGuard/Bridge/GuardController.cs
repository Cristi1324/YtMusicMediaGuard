using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace YtMusicMediaGuard;

internal sealed class GuardController : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProfileState> _profiles = new();
    private readonly List<HostConnection> _connections = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _pipeName;
    private NamedPipeServerStream? _waiting;
    private System.Threading.Timer? _watch;
    private volatile bool _armed;
    private volatile bool _hookActive;

    public bool IsArmed => _armed;
    public int TabCount { get; private set; }
    public bool HookConnected
    {
        get
        {
            lock (_gate) return _connections.Any(connection => connection.IsHook);
        }
    }

    public event Action? Changed;
    public event Action<MediaAction>? HookKeyPressed;

    public GuardController(string? pipeName = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? FramePipe.Name : pipeName;
    }

    public void Start()
    {
        _watch = new System.Threading.Timer(_ => Watch(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        Task.Run(AcceptLoop);
        Publish();
    }

    public void PublishHookState(bool active)
    {
        List<HostConnection> hooks;
        lock (_gate)
        {
            _hookActive = active;
            hooks = _connections.Where(connection => connection.IsHook).ToList();
        }

        SendHookState(hooks);
    }

    public void StopHook()
    {
        List<HostConnection> hooks;
        lock (_gate) hooks = _connections.Where(connection => connection.IsHook).ToList();
        var body = JsonSerializer.SerializeToUtf8Bytes(new BridgeMessage { Type = "hookStop" }, BridgeJson.Options);
        foreach (var hook in hooks)
        {
            try { hook.Send(body); }
            catch (Exception ex) { Logger.Error("Could not stop the elevated hook. " + ex.Message); }
        }
    }

    public void SendCommand(MediaAction action)
    {
        var command = MediaCommands.Name(action);

        HostConnection? target;
        lock (_gate)
        {
            var fresh = _connections.Where(IsLive).ToList();
            target = fresh.FirstOrDefault(connection => ProfileOf(connection)?.Audible == true)
                ?? fresh.OrderByDescending(connection => ProfileOf(connection)?.LastActive ?? 0).FirstOrDefault();
        }

        if (target is null)
        {
            Logger.Error($"Dropped {command}; the extension is not connected.");
            return;
        }

        try
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(new BridgeMessage { Type = "command", Command = command }, BridgeJson.Options);
            target.Send(body);
            Logger.Info($"Sent {command} to the extension.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not send {command}. " + ex.Message);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _watch?.Dispose();
        NamedPipeServerStream? waiting;
        List<HostConnection> connections;
        lock (_gate)
        {
            waiting = _waiting;
            connections = _connections.ToList();
        }

        try { waiting?.Dispose(); } catch { /* Unblocks the accept loop. */ }
        foreach (var connection in connections)
            connection.Dispose();
        _cts.Dispose();
    }

    private void AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServer();
                lock (_gate) _waiting = server;
                server.WaitForConnection();
                lock (_gate)
                {
                    if (ReferenceEquals(_waiting, server)) _waiting = null;
                }

                var connected = server;
                server = null;
                Task.Run(() => Listen(connected));
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                Logger.Error("Pipe accept failed: " + ex.Message);
                try { server?.Dispose(); } catch { /* Already closed. */ }
                Thread.Sleep(400);
            }
            catch
            {
                try { server?.Dispose(); } catch { /* Shutting down. */ }
                break;
            }
        }
    }

    private void Listen(NamedPipeServerStream server)
    {
        var connection = new HostConnection(server);
        lock (_gate) _connections.Add(connection);
        Logger.Info("Extension host connected.");
        try
        {
            while (FramePipe.Read(server) is byte[] body)
                Handle(connection, body);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Logger.Info("Extension host disconnected.");
        }
        finally
        {
            lock (_gate) _connections.Remove(connection);
            connection.Dispose();
        }
    }

    private void Handle(HostConnection connection, byte[] body)
    {
        if (!TryReadMessage(body, out var message))
        {
            Logger.Error("Ignored a bad extension message.");
            return;
        }

        if (message.Type == "hook")
        {
            List<HostConnection> hooks;
            lock (_gate)
            {
                connection.IsHook = true;
                hooks = new List<HostConnection> { connection };
            }

            Logger.Error("Elevated hook connected.");
            SendHookState(hooks);
            return;
        }

        if (message.Type == "hookKey")
        {
            if (MediaCommands.TryParse(message.Command, out var action))
                HookKeyPressed?.Invoke(action);
            return;
        }

        if (message.Type != "state" || string.IsNullOrWhiteSpace(message.ProfileId)) return;
        lock (_gate)
        {
            connection.ProfileId = message.ProfileId;
            _profiles[message.ProfileId] = new ProfileState(message.OpenTabs, message.Audible, message.LastActive, DateTime.UtcNow);
        }

        Publish();
    }

    private void Watch()
    {
        try
        {
            var chromeRunning = Process.GetProcessesByName("chrome").Length > 0;
            var changed = false;
            lock (_gate)
            {
                if (!chromeRunning && _profiles.Count > 0)
                {
                    _profiles.Clear();
                    changed = true;
                }

                var cutoff = DateTime.UtcNow.AddMinutes(-3);
                var stale = _profiles.Where(pair => pair.Value.LastSeen < cutoff).Select(pair => pair.Key).ToList();
                if (stale.Count > 0)
                {
                    foreach (var id in stale) _profiles.Remove(id);
                    changed = true;
                }
            }

            if (changed) Publish();
        }
        catch (Exception ex)
        {
            Logger.Error("Watch failed: " + ex.Message);
        }
    }

    private void Publish()
    {
        int tabs;
        bool armed;
        lock (_gate)
        {
            armed = ComputeArmed();
            tabs = _profiles.Values.Where(IsFresh).Sum(profile => Math.Max(0, profile.OpenTabs));
        }

        if (armed != _armed)
            Logger.Info(armed ? $"Armed ({tabs} YouTube Music tab(s))." : "Idle.");
        _armed = armed;
        TabCount = tabs;
        try { Changed?.Invoke(); } catch (Exception ex) { Logger.Error(ex.Message); }
    }

    private bool ComputeArmed() => _profiles.Values.Any(profile => profile.OpenTabs > 0 && IsFresh(profile));

    private bool IsLive(HostConnection connection)
    {
        var profile = ProfileOf(connection);
        return profile is not null && profile.OpenTabs > 0 && IsFresh(profile);
    }

    private ProfileState? ProfileOf(HostConnection connection) =>
        !connection.IsHook && connection.ProfileId is not null && _profiles.TryGetValue(connection.ProfileId, out var profile) ? profile : null;

    private void SendHookState(List<HostConnection> hooks)
    {
        if (hooks.Count == 0) return;
        var active = _hookActive;
        var body = JsonSerializer.SerializeToUtf8Bytes(new BridgeMessage { Type = "hookState", Armed = active }, BridgeJson.Options);
        foreach (var hook in hooks)
        {
            try { hook.Send(body); }
            catch (Exception ex) { Logger.Error("Could not update the elevated hook. " + ex.Message); }
        }
    }

    private static bool IsFresh(ProfileState profile) => DateTime.UtcNow - profile.LastSeen < TimeSpan.FromMinutes(3);

    private NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private static bool TryReadMessage(byte[] body, out BridgeMessage message)
    {
        message = new BridgeMessage();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            var root = doc.RootElement;
            message.Type = ReadString(root, "type");
            message.ProfileId = ReadString(root, "profileId");
            message.Command = ReadString(root, "command");
            if (root.TryGetProperty("openTabs", out var tabs) && tabs.TryGetInt32(out var count))
                message.OpenTabs = count;
            message.Audible = root.TryGetProperty("audible", out var audible) && audible.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("lastActive", out var last) && last.ValueKind == JsonValueKind.Number && last.TryGetDouble(out var stamp))
                message.LastActive = stamp;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record ProfileState(int OpenTabs, bool Audible, double LastActive, DateTime LastSeen);

    private sealed class HostConnection : IDisposable
    {
        private readonly Stream _stream;
        private readonly object _write = new();

        public HostConnection(Stream stream) => _stream = stream;

        public string? ProfileId { get; set; }
        public bool IsHook { get; set; }

        public void Send(byte[] body)
        {
            lock (_write) FramePipe.Write(_stream, body);
        }

        public void Dispose()
        {
            try { _stream.Dispose(); } catch { /* Already closed. */ }
        }
    }
}
