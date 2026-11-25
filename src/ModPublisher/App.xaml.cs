using System.IO;
using System.Windows;
using System.Windows.Threading;
using ModPublisher.Services;

namespace ModPublisher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        L.Parse(e.Args);
        // Dates et nombres au format de Windows dans le XAML
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag)));
        Views.ScreenshotMode.Parse(e.Args);
        Views.ProbeMode.Parse(e.Args);
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        File.AppendAllText(Path.Combine(AppPaths.Data, "crash.log"), $"{DateTime.Now:s} {e.Exception}\n\n");
        MessageBox.Show(e.Exception.Message, L.T("Erreur inattendue", "Unexpected error"), MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
