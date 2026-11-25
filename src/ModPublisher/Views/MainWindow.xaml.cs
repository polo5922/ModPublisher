using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using ModPublisher.Services;
using ModPublisher.ViewModels;

namespace ModPublisher.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        SourceInitialized += (_, _) => _vm.S.CurseForgeSite.Attach(new WindowInteropHelper(this).Handle);
        Closing += OnClosing;
        Loaded += async (_, _) => await OnLoadedAsync();
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            if (Preview.CoreWebView2 is not null) Preview.NavigateToString(_vm.Page.PreviewHtml);
        };
        _vm.Page.PropertyChanged += OnPageChanged;
    }

    private async Task OnLoadedAsync()
    {
        await Preview.EnsureCoreWebView2Async(await WebViewHost.EnvironmentAsync());
        // Les liens de l'aperçu s'ouvrent dans le navigateur, pas dans l'aperçu
        Preview.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (e.IsUserInitiated && e.Uri.StartsWith("http"))
            {
                e.Cancel = true;
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            }
        };
        Preview.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
        };
        _previewTimer.Start();
        if (ScreenshotMode.Folder is not null) _ = ScreenshotMode.RunAsync(this, Tabs, Preview);
        if (ScreenshotMode.Demo) { ScreenshotMode.FillDemo(_vm); return; }
        if (ProbeMode.Folder is not null) { _ = ProbeMode.RunAsync(this, _vm); return; }
        await _vm.RunAsync(L.T("Vérification des comptes", "Checking accounts"), _vm.Accounts.RefreshAsync);
    }

    private bool _shutdownDone;

    /// <summary>Garde la session CurseForge et ferme le navigateur caché avant de quitter (2 s au plus).</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownDone) return;
        e.Cancel = true;
        _shutdownDone = true;
        await Task.WhenAny(_vm.S.CurseForgeSite.ShutdownAsync(), Task.Delay(2000));
        Close();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageViewModel.Description) or nameof(PageViewModel.PreviewHtml))
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        }
    }

    private int _journalLength;

    private void Journal_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Une nouvelle ligne ouvre le journal (sauf quand on vient de l'effacer)
        if (JournalBox.Text.Length > _journalLength && ScreenshotMode.Folder is null) JournalPanel.IsExpanded = true;
        _journalLength = JournalBox.Text.Length;
        JournalBox.ScrollToEnd();
    }
}
