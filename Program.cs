using System.Windows.Forms;

namespace PhotoSaverAnimated;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string arg = args.Length > 0 ? args[0].ToLower().Trim() : "/c";

        if (arg == "/s" || arg == "-s")
        {
            RunScreensaver();
        }
        else if ((arg == "/p" || arg == "-p") && args.Length > 1 && long.TryParse(args[1], out long hwnd))
        {
            Application.Run(new ScreenSaverForm(new IntPtr(hwnd)));
        }
        else
        {
            Application.Run(new ConfigForm());
        }
    }

    static void RunScreensaver()
    {
        // Determine which screens to cover
        Screen[] targets = AppSettings.MonitorDisplay == MonitorMode.Primary
            ? new[] { Screen.PrimaryScreen! }
            : Screen.AllScreens;

        if (targets.Length == 1)
        {
            Application.Run(new ScreenSaverForm(targets[0]));
            return;
        }

        // Multi-monitor: one form per screen
        var forms = targets.Select(s => new ScreenSaverForm(s)).ToList();

        // Any screen's exit → Application.Exit() closes every open form cleanly.
        // Application.Exit is idempotent and designed for cross-form shutdown.
        foreach (var form in forms)
            form.ExitRequested += Application.Exit;

        // Safety net: if the primary form closes for any external reason
        // (OS session end, crash, etc.), close the secondary forms directly.
        // We skip forms[0] here to avoid closing it again while it's already closing.
        forms[0].FormClosed += (_, _) =>
            forms.Skip(1).ToList().ForEach(f => { if (!f.IsDisposed) f.Close(); });

        // Show secondary forms first, then run the message loop on the primary
        forms.Skip(1).ToList().ForEach(f => f.Show());
        Application.Run(forms[0]);
    }
}
