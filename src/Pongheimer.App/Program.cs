using System.Globalization;
using Pongheimer.Core.Diagnostics;
using Pongheimer.Infrastructure;
using Pongheimer.Localization;
using Forms = System.Windows.Forms;

namespace Pongheimer;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "oPongheimer");
        var log = new FileLog(Path.Combine(dataDirectory, "log.txt"));
        log.Info("Application started");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Error("Fatal unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            using var game = new PongheimerGame(LaunchOptions.Parse(args), log, dataDirectory);
            game.Run();
        }
        catch (Exception ex)
        {
            // Збої всередині кадру гра переживає сама; сюди доходить лише те, з чим вона не змогла
            // навіть запуститись (наприклад, немає відеодрайвера). Чесно повідомляємо й виходимо.
            log.Error("Game could not start or crashed", ex);
            var texts = new Localizer(Localizer.Resolve(null, CultureInfo.CurrentUICulture)).Current;
            Forms.MessageBox.Show(
                texts.StartupFailed + "\n\n" + ex.Message,
                "oPongheimer",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }
}
