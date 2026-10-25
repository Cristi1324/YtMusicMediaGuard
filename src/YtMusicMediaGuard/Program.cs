namespace YtMusicMediaGuard;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run();

        if (args.Any(arg => arg.Equals("--install-hook", StringComparison.OrdinalIgnoreCase)))
            return HookElevation.InstallElevated();

        if (args.Any(arg => arg.Equals("--hook", StringComparison.OrdinalIgnoreCase)))
            return HookAgent.Run();

        var forceTray = args.Any(arg => arg.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (!forceTray && Stdio.InputIsPipe())
        {
            try
            {
                NativeHost.Run();
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
                return 1;
            }
        }

        using var mutex = SingleInstance.Acquire();
        if (mutex is null)
        {
            Logger.Info("Tray already running.");
            return 0;
        }

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex.ToString());
            MessageBox.Show(ex.Message, "YouTube Music media key guard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch { /* The mutex is abandoned if shutdown already released it. */ }
        }
    }
}

internal static class SingleInstance
{
    public static Mutex? Acquire()
    {
        var mutex = new Mutex(false, @"Local\YtMusicMediaGuard");
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
}
