using System.IO.Pipes;
using System.Text.Json;

namespace YtMusicMediaGuard;

internal static class HookAgent
{
    public static int Run()
    {
        if (!HookElevation.IsCurrentElevated())
        {
            Logger.Error("The media-key helper is not running as administrator.");
            return 1;
        }

        using var mutex = Acquire();
        if (mutex is null)
        {
            Logger.Error("The elevated hook is already running.");
            return 0;
        }

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.Run(new HookContext());
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex.ToString());
            return 1;
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch { /* Shutdown already released it. */ }
        }
    }

    private static Mutex? Acquire()
    {
        var mutex = new Mutex(false, @"Local\YtMusicMediaGuard.Hook");
        try
        {
            if (mutex.WaitOne(0)) return mutex;
        }
        catch (AbandonedMutexException)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    private sealed class HookContext : ApplicationContext
    {
        private readonly Control _invoker;
        private readonly KeyboardHook _hook;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _write = new();
        private NamedPipeClientStream? _pipe;
        private volatile bool _active;
        private bool _loggedWait;

        public HookContext()
        {
            _invoker = new Control();
            _ = _invoker.Handle;
            VolumeGuard.Start();
            FocusDetector.Start();
            _hook = new KeyboardHook(_invoker)
            {
                IsActive = () => _active && FocusDetector.ForegroundElevated,
                OnAction = SendKey,
            };
            _hook.Start();
            var thread = new Thread(ConnectLoop) { IsBackground = true, Name = "HookPipe" };
            thread.Start();
            Logger.Error("Elevated hook is running.");
        }

        protected override void ExitThreadCore()
        {
            _cts.Cancel();
            _hook.Dispose();
            FocusDetector.Stop();
            VolumeGuard.Stop();
            lock (_write)
            {
                try { _pipe?.Dispose(); } catch { /* The read loop unblocks. */ }
                _pipe = null;
            }

            _invoker.Dispose();
            _cts.Dispose();
            base.ExitThreadCore();
        }

        private void ConnectLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                NamedPipeClientStream? client = null;
                try
                {
                    client = new NamedPipeClientStream(".", FramePipe.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
                    client.Connect(2000);
                    var hello = JsonSerializer.SerializeToUtf8Bytes(new BridgeMessage { Type = "hook" }, BridgeJson.Options);
                    lock (_write)
                    {
                        _pipe = client;
                        FramePipe.Write(client, hello);
                    }

                    _loggedWait = false;
                    while (!_cts.IsCancellationRequested && FramePipe.Read(client) is byte[] body)
                    {
                        if (!TryRead(body, out var message)) continue;
                        if (message.Type == "hookStop")
                        {
                            try { _invoker.BeginInvoke(ExitThread); } catch { /* The helper is already closing. */ }
                            return;
                        }

                        if (message.Type == "hookState")
                            _active = message.Armed;
                    }
                }
                catch (Exception ex) when (!_cts.IsCancellationRequested)
                {
                    if (!_loggedWait)
                    {
                        _loggedWait = true;
                        Logger.Error("Elevated hook is waiting for the tray app. " + ex.Message);
                    }
                }
                finally
                {
                    _active = false;
                    lock (_write)
                    {
                        if (ReferenceEquals(_pipe, client)) _pipe = null;
                    }

                    try { client?.Dispose(); } catch { /* Already closed. */ }
                }

                if (!_cts.IsCancellationRequested)
                    Thread.Sleep(500);
            }
        }

        private void SendKey(MediaAction action)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(
                new BridgeMessage { Type = "hookKey", Command = MediaCommands.Name(action) },
                BridgeJson.Options);
            lock (_write)
            {
                if (_pipe is null) return;
                try
                {
                    FramePipe.Write(_pipe, body);
                }
                catch (Exception ex)
                {
                    Logger.Error("Elevated hook could not forward a media key. " + ex.Message);
                }
            }
        }

        private static bool TryRead(byte[] body, out BridgeMessage message)
        {
            try
            {
                message = JsonSerializer.Deserialize<BridgeMessage>(body, BridgeJson.Options) ?? new BridgeMessage();
                return true;
            }
            catch (JsonException)
            {
                message = new BridgeMessage();
                return false;
            }
        }
    }
}
