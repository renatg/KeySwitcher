using KeySwitcher.Settings;

namespace KeySwitcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\KeySwitcher.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            if (!args.Contains(AutoStart.Argument))
                MessageBox.Show("KeySwitcher уже запущен — значок в области уведомлений.", "KeySwitcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        bool firstRun = !AppSettings.Exists;
        var settings = AppSettings.Load();
        if (firstRun)
        {
            settings.Save();
            AutoStart.Set(true);
        }

        Application.Run(new TrayContext(settings, firstRun));
    }
}
