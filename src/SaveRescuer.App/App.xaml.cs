using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SaveRescuer.App;

public partial class App : Application
{
    /// <summary>
    /// A save passed on the command line, which is what Windows does when a .fos file is
    /// dropped onto the executable or opened with it.
    /// </summary>
    public static string? StartupSave { get; private set; }

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FO4SaveRescuer", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupSave = e.Args.FirstOrDefault(a =>
            a.EndsWith(".fos", StringComparison.OrdinalIgnoreCase) && File.Exists(a));

        // Without this a single unexpected exception kills the window with no explanation.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write(args.ExceptionObject as Exception, fatal: true);

        base.OnStartup(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Write(e.Exception, fatal: false);
        MessageBox.Show(
            $"{e.Exception.Message}\n\nThe details were written to:\n{LogPath}",
            "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;                 // keep the window alive
    }

    private static void Write(Exception? ex, bool fatal)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(fatal ? "FATAL " : "")}{ex}{System.Environment.NewLine}{System.Environment.NewLine}");
        }
        catch { /* logging must never make things worse */ }
    }
}
