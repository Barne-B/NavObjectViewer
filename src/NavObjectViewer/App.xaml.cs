using System.Windows;
using System.Windows.Threading;
using NavObjectViewer.Core.Services;

namespace NavObjectViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // CP850 (OEM) codepage is not built into .NET Core — register the provider once.
        Encodings.Register();

        // Never let an unexpected failure silently kill the app; log it and tell the user.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logging.Log("AppDomain.UnhandledException", args.ExceptionObject as Exception
                ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown"));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logging.Log("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logging.Log("DispatcherUnhandledException", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            $"An unexpected error occurred and was logged:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\nLog: {Logging.LogFilePath}",
            "NAV Object Viewer — error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
