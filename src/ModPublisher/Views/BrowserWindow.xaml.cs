using System.Windows;
using ModPublisher.Services;

namespace ModPublisher.Views;

/// <summary>
/// Fenêtre de navigation (profil WebView2 partagé) : sert à se connecter aux sites, et en option à enregistrer les
/// appels d'API du site (mise au point de l'API interne CurseForge).
/// </summary>
public partial class BrowserWindow : Window
{
    private readonly string _url;
    private readonly Func<BrowserWindow, Task<bool>>? _isDone;
    private NetRecorder? _recorder;
    private readonly string? _recordHost;

    /// <param name="isDone">Testé après chaque page chargée : si vrai, la fenêtre se ferme toute seule (connexion réussie).</param>
    public BrowserWindow(string title, string url, string hint, Func<BrowserWindow, Task<bool>>? isDone = null, string? recordHost = null)
    {
        InitializeComponent();
        Title = title;
        Hint.Text = hint;
        _url = url;
        _isDone = isDone;
        _recordHost = recordHost;
        if (recordHost is not null) RecordButton.Visibility = Visibility.Visible;
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => _recorder?.Stop();
    }

    public Microsoft.Web.WebView2.Core.CoreWebView2 Core => Web.CoreWebView2;

    private async Task StartAsync()
    {
        await Web.EnsureCoreWebView2Async(await WebViewHost.EnvironmentAsync());
        Web.CoreWebView2.SourceChanged += (_, _) => Address.Text = Web.Source?.ToString();
        Web.CoreWebView2.NavigationCompleted += async (_, _) =>
        {
            if (!_closing && _isDone is not null && await _isDone(this)) Finish(true);
        };
        Web.CoreWebView2.Navigate(_url);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack();
    }

    private async void Done_Click(object sender, RoutedEventArgs e) => Finish(_isDone is null || await _isDone(this));

    private bool _closing;

    private void Finish(bool success)
    {
        if (_closing) return;
        _closing = true;
        DialogResult = success;
    }

    private void Record_Changed(object sender, RoutedEventArgs e)
    {
        if (RecordButton.IsChecked == true && _recordHost is not null)
        {
            _recorder = new NetRecorder(Web.CoreWebView2, _recordHost);
            _recorder.Recorded += line => Dispatcher.Invoke(() =>
                Hint.Text = L.T($"{_recorder.Count} appels enregistrés dans {AppPaths.NetLog} — dernier : {line}", $"{_recorder.Count} calls recorded in {AppPaths.NetLog} — latest: {line}"));
            Hint.Text = L.T("Enregistrement en cours : fais les modifications sur le site, puis clique Terminé.", "Recording: make your changes on the site, then click Done.");
        }
        else
        {
            _recorder?.Stop();
            _recorder = null;
        }
    }
}
