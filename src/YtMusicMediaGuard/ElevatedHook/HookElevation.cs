using System.ComponentModel;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace YtMusicMediaGuard;

internal static class HookElevation
{
    public const string TaskName = "YtMusicMediaGuard.Hook";

    private static DateTime _lastRunUtc = DateTime.MinValue;
    private static DateTime _lastQueryUtc = DateTime.MinValue;
    private static bool _taskExists;
    private static int _prompting;
    private static bool _declined;

    public static bool IsCurrentElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void Ensure(bool hookConnected, bool foregroundElevated)
    {
        if (hookConnected) return;
        if (TaskExists())
        {
            if (DateTime.UtcNow - _lastRunUtc < TimeSpan.FromSeconds(10)) return;
            _lastRunUtc = DateTime.UtcNow;
            RunTask();
            return;
        }

        if (!foregroundElevated || _declined || Interlocked.CompareExchange(ref _prompting, 1, 0) != 0) return;
        Task.Run(() =>
        {
            try { RequestInstall(); }
            finally { Interlocked.Exchange(ref _prompting, 0); }
        });
    }

    public static int InstallElevated()
    {
        if (!IsCurrentElevated())
        {
            Logger.Error("The elevated hook could not be installed without administrator permission.");
            return 1;
        }

        var xmlPath = Path.Combine(Path.GetTempPath(), "YtMusicMediaGuard.Hook.xml");
        File.WriteAllText(xmlPath, TaskXml(), Encoding.Unicode);
        try
        {
            var code = Run($"/Create /F /TN \"{TaskName}\" /XML \"{xmlPath}\"", out var output);
            if (code != 0)
            {
                Logger.Error("Could not register the elevated hook. " + output.Trim());
                return 1;
            }
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* The task is already registered. */ }
        }

        Logger.Error("Registered the elevated media-key hook.");
        RunTask();
        return 0;
    }

    private static void RequestInstall()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.ExecutablePath,
                Arguments = "--install-hook",
                UseShellExecute = true,
                Verb = "runas",
            });
            if (process is null)
            {
                Decline("The elevated hook was not installed, so administrator games keep the media keys.");
                return;
            }

            process.WaitForExit();
            _lastQueryUtc = DateTime.MinValue;
            if (process.ExitCode != 0)
                Decline("The elevated hook was not installed, so administrator games keep the media keys.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Decline("The elevated hook was not allowed, so administrator games keep the media keys.");
        }
        catch (Exception ex)
        {
            Decline("Could not ask for the elevated hook. " + ex.Message);
        }
    }

    private static void Decline(string message)
    {
        _declined = true;
        Logger.Error(message);
    }

    private static bool TaskExists()
    {
        if (DateTime.UtcNow - _lastQueryUtc < TimeSpan.FromSeconds(5)) return _taskExists;
        var code = Run($"/Query /TN \"{TaskName}\"", out _);
        _taskExists = code == 0;
        _lastQueryUtc = DateTime.UtcNow;
        return _taskExists;
    }

    private static void RunTask()
    {
        var code = Run($"/Run /TN \"{TaskName}\"", out var output);
        if (code != 0)
            Logger.Error("Could not start the elevated hook. " + output.Trim());
    }

    private static string TaskXml()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name) ?? "";
        var command = SecurityElement.Escape(AppPaths.ExecutablePath) ?? "";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Forwards media keys to YouTube Music while an administrator app is focused.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <Arguments>--hook</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static int Run(string arguments, out string output)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("schtasks", arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(8000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* Already gone. */ }
                output = "schtasks timed out.";
                return -1;
            }

            output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return -1;
        }
    }
}
