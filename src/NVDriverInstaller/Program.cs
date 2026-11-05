using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using NVDriverInstaller.Services;

namespace NVDriverInstaller;

public static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    public static void Main(string[] args)
    {
        // Handle CLI verbs or --resume headlessly without booting WPF window
        if (args.Length > 0 && !args[0].StartsWith("/"))
        {
            if (!Console.IsOutputRedirected)
            {
                if (!AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    AllocConsole();
                }

                try
                {
                    var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                    Console.SetOut(stdOut);
                    var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                    Console.SetError(stdErr);
                }
                catch
                {
                    // Ignore stream setup failures
                }
            }

            if (args.Any(a => a.Equals("--resume", StringComparison.OrdinalIgnoreCase)))
            {
                var progress = new Progress<string>(Console.WriteLine);
                var (ok, _) = WorkflowCoordinator.ResumeWorkflowAsync(progress).GetAwaiter().GetResult();
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }

            CliDispatcher.RunAsync(args).GetAwaiter().GetResult();
            return;
        }

        // GUI execution: Force SoftwareOnly rendering for Safe Mode & display teardown resilience
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                AppLogger.LogError("AppDomain UnhandledException", ex);
            }
        };

        AppLogger.Log($"NVDriver Installer starting (PID {Environment.ProcessId}, OS: {Environment.OSVersion})");

        var app = new App();
        app.DispatcherUnhandledException += (s, e) =>
        {
            AppLogger.LogError("DispatcherUnhandledException", e.Exception);
        };

        app.InitializeComponent();

        var mainWindow = new MainWindow();
        app.Run(mainWindow);
        AppLogger.Log("NVDriver Installer shut down cleanly.");
    }
}
