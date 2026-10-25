using System.IO.Pipes;
using System.Text;

namespace YtMusicMediaGuard;

internal static class SelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        Check(failures, "extension id", () =>
        {
            var id = ExtensionIdentity.CurrentId();
            if (id != ExtensionIdentity.ExpectedId)
                throw new InvalidOperationException($"Expected {ExtensionIdentity.ExpectedId} but computed {id}.");
        });
        Check(failures, "shortcut roundtrip", () =>
        {
            foreach (var text in new[]
            {
                "Ctrl+Alt+Shift+P",
                "Ctrl+Alt+Shift+N",
                "Ctrl+Alt+Shift+B",
                "Ctrl+Alt+Shift+Up",
                "Ctrl+Alt+Shift+Down",
                "Ctrl+Alt+Shift+M",
            })
            {
                var formatted = Chord.Parse(text).Format();
                if (formatted != text) throw new InvalidOperationException($"{text} became {formatted}.");
            }
        });
        Check(failures, "input struct", () =>
        {
            var expected = IntPtr.Size == 8 ? 40 : 28;
            if (ChordSender.InputSize != expected)
                throw new InvalidOperationException($"INPUT size is {ChordSender.InputSize}, expected {expected}.");
        });
        Check(failures, "frame pipe", () =>
        {
            using var stream = new MemoryStream();
            var payload = Encoding.UTF8.GetBytes("{\"type\":\"state\"}");
            FramePipe.Write(stream, payload);
            stream.Position = 0;
            var read = FramePipe.Read(stream) ?? throw new InvalidOperationException("Frame was empty.");
            if (!payload.AsSpan().SequenceEqual(read))
                throw new InvalidOperationException("Frame bytes did not round-trip.");
        });
        Check(failures, "keyboard hook", () =>
        {
            Application.EnableVisualStyles();
            var invoker = new Control();
            _ = invoker.Handle;
            using var hook = new KeyboardHook(invoker) { IsActive = () => false };
            hook.Start();
            if (!hook.IsRunning) throw new InvalidOperationException("The hook thread stopped immediately.");
        });
        Check(failures, "volume endpoint", () =>
        {
            if (!VolumeGuard.TryReadLevel(out var level, out _))
                throw new InvalidOperationException("The default audio endpoint could not be read.");
            if (level is < 0 or > 1)
                throw new InvalidOperationException($"Endpoint volume {level} is outside 0..1.");
        });
        Check(failures, "fullscreen window", () =>
        {
            if (!FocusDetector.CoversScreen(0, 0, 1920, 1080, 0, 0, 1920, 1080))
                throw new InvalidOperationException("A window that fills the monitor was not treated as fullscreen.");
            if (FocusDetector.CoversScreen(0, 0, 1920, 1040, 0, 0, 1920, 1080))
                throw new InvalidOperationException("A maximized window above the taskbar was treated as fullscreen.");
            var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "game.exe" };
            if (!FocusDetector.IsListedGame(@"C:\Games\game.exe", games))
                throw new InvalidOperationException("A known game executable was not recognized.");
            if (FocusDetector.IsListedGame(@"C:\Windows\notepad.exe", games))
                throw new InvalidOperationException("An unrelated executable was treated as a game.");
        });
        Check(failures, "guard pipe", TestGuardPipe);
        Check(failures, "elevated hook message", TestHookMessage);

        if (failures.Count == 0)
        {
            Console.WriteLine("self-test ok");
            return 0;
        }

        Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
        return 1;
    }

    private static void TestGuardPipe()
    {
        const string pipeName = "YtMusicMediaGuard.SelfTest";
        using var guard = new GuardController(pipeName);
        guard.Start();
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        client.Connect(3000);
        var state = Encoding.UTF8.GetBytes(
            "{\"type\":\"state\",\"profileId\":\"self-test\",\"openTabs\":1,\"audible\":true,\"lastActive\":1761390123456.75}");
        FramePipe.Write(client, state);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!guard.IsArmed && DateTime.UtcNow < deadline)
            Thread.Sleep(20);
        if (!guard.IsArmed)
            throw new InvalidOperationException("The guard did not arm after a YouTube Music tab report.");

        using var received = new ManualResetEventSlim(false);
        BridgeMessage? command = null;
        var reader = Task.Run(() =>
        {
            try
            {
                var body = FramePipe.Read(client);
                if (body is null) return;
                command = JsonSerializer.Deserialize<BridgeMessage>(body, BridgeJson.Options);
                received.Set();
            }
            catch (IOException)
            {
                // The test closed the client.
            }
        });

        guard.SendCommand(MediaAction.PlayPause);
        if (!received.Wait(TimeSpan.FromSeconds(3)))
            throw new InvalidOperationException("The player command was not delivered to the extension host.");
        if (command?.Command != "playPause")
            throw new InvalidOperationException($"Expected playPause but received {command?.Command ?? "nothing"}.");
        reader.Wait(TimeSpan.FromSeconds(1));
    }

    private static void TestHookMessage()
    {
        const string pipeName = "YtMusicMediaGuard.SelfTest.Hook";
        using var guard = new GuardController(pipeName);
        MediaAction? received = null;
        using var ready = new ManualResetEventSlim(false);
        guard.HookKeyPressed += action =>
        {
            received = action;
            ready.Set();
        };
        guard.Start();
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        client.Connect(3000);
        FramePipe.Write(client, Encoding.UTF8.GetBytes("{\"type\":\"hook\"}"));
        var state = FramePipe.Read(client) ?? throw new InvalidOperationException("The elevated hook received no state.");
        var message = JsonSerializer.Deserialize<BridgeMessage>(state, BridgeJson.Options);
        if (message?.Type != "hookState")
            throw new InvalidOperationException($"Expected hookState but received {message?.Type ?? "nothing"}.");
        FramePipe.Write(client, Encoding.UTF8.GetBytes("{\"type\":\"hookKey\",\"command\":\"next\"}"));
        if (!ready.Wait(TimeSpan.FromSeconds(3)))
            throw new InvalidOperationException("The elevated hook key was not delivered.");
        if (received != MediaAction.Next)
            throw new InvalidOperationException($"Expected next but received {received?.ToString() ?? "nothing"}.");
        if (guard.IsArmed)
            throw new InvalidOperationException("The elevated hook armed the guard without a YouTube Music tab.");
    }

    private static void Check(List<string> failures, string name, Action test)
    {
        try
        {
            test();
        }
        catch (Exception ex)
        {
            failures.Add(name + ": " + ex.Message);
        }
    }
}
