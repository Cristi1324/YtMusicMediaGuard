using System.IO.Pipes;

namespace YtMusicMediaGuard;

internal static class NativeHost
{
    public static void Run()
    {
        Logger.Info("Native host started.");
        var pipe = Connect();
        Logger.Info("Native host connected to the tray app.");
        using var input = Stdio.OpenInput();
        using var output = Stdio.OpenOutput();
        var toPipe = Task.Run(() => Pump(input, pipe, "Chrome"));
        var toChrome = Task.Run(() => Pump(pipe, output, "tray"));
        Task.WaitAny(toPipe, toChrome);
        try { input.Dispose(); } catch { /* Unblock the Chrome read. */ }
        try { pipe.Dispose(); } catch { /* Unblock the tray read. */ }
        Task.WaitAll(new[] { toPipe, toChrome }, TimeSpan.FromSeconds(2));
        Logger.Info("Native host exiting.");
    }

    private static NamedPipeClientStream Connect()
    {
        var pipe = TryConnect(TimeSpan.FromSeconds(2));
        if (pipe is not null) return pipe;

        TrayLauncher.Start();
        pipe = TryConnect(TimeSpan.FromSeconds(10));
        if (pipe is not null) return pipe;
        throw new IOException("The tray app did not open its pipe.");
    }

    private static NamedPipeClientStream? TryConnect(TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            var pipe = new NamedPipeClientStream(".", FramePipe.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(400);
                return pipe;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                pipe.Dispose();
            }
        }

        return null;
    }

    private static void Pump(Stream input, Stream output, string source)
    {
        try
        {
            FramePipe.Copy(input, output);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Logger.Info($"Host stopped the {source} stream.");
        }
    }
}
