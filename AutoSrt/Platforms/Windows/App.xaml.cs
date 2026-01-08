using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace AutoSrt.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // Capture early startup failures (often WebView2 / runtime dependency resolution issues)
            this.UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            this.InitializeComponent();
        }

        private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.Exception;
                var details = ex is FileNotFoundException fnf
                    ? $"{ex}\n\nFileName: {fnf.FileName}" 
                    : ex.ToString();

                System.Diagnostics.Debug.WriteLine("[UnhandledException] " + details);

                _ = Microsoft.Maui.Controls.Application.Current?.MainPage?.Dispatcher.DispatchAsync(async () =>
                {
                    try
                    {
                        await Microsoft.Maui.Controls.Application.Current.MainPage.DisplayAlert("UnhandledException", details, "OK");
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }

        private static void OnCurrentDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[AppDomain.UnhandledException] " + e.ExceptionObject);
            }
            catch
            {
            }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[TaskScheduler.UnobservedTaskException] " + e.Exception);
            }
            catch
            {
            }
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}
