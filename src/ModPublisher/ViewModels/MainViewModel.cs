using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ModPublisher.Services;
using ModPublisher.Views;

namespace ModPublisher.ViewModels;

public sealed class ModItem(ModPage page) : ObservableObject
{
    public ModPage Page { get; } = page;
    public string Name => Page.Name;
    public string Folder => Page.Folder;
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string Version => Page.GradleVersion() is { } v ? "v" + v : "";
    public string Info => string.Join(" · ", Page.Loaders.Select(l => l.Length > 0 ? char.ToUpper(l[0]) + l[1..] : l)
        .Concat(Page.GameVersions.Take(1).Select(v => "MC " + v)));

    /// <summary>Redessine l'icône en haut et dans la liste après un changement.</summary>
    public void RefreshIcon() => OnPropertyChanged(nameof(Icon));

    /// <summary>Icône du mod (page ou fabric.mod.json), chargée sans verrouiller le fichier.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (Page.IconPath is null) return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.DecodePixelWidth = 96;
                image.UriSource = new Uri(Page.IconPath);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
        }
    }
}

/// <summary>Services partagés par tous les onglets.</summary>
public sealed class AppServices
{
    public ApiHttp Http { get; } = new();
    public SecretStore Secrets { get; } = new();
    public ProjectStore Projects { get; } = new();
    public ModrinthClient Modrinth { get; }
    public CurseForgeUploadClient CurseForgeUpload { get; }
    public CurseForgeSite CurseForgeSite { get; }
    public CurseForgeAuthors CurseForge { get; }

    public AppServices()
    {
        Modrinth = new ModrinthClient(Http, () => Secrets.Get(SecretStore.ModrinthToken));
        CurseForgeUpload = new CurseForgeUploadClient(Http, () => Secrets.Get(SecretStore.CurseForgeUploadToken));
        CurseForgeSite = new CurseForgeSite(Http, Secrets);
        CurseForge = new CurseForgeAuthors(CurseForgeSite);
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    public AppServices S { get; } = new();
    public ObservableCollection<ModItem> Mods { get; } = new();

    public AccountsViewModel Accounts { get; }
    public SettingsViewModel Settings { get; }
    public PageViewModel Page { get; }
    public VersionsViewModel Versions { get; }
    public GalleryViewModel Gallery { get; }
    public StatsViewModel Stats { get; }

    [ObservableProperty] private ModItem? _selectedMod;
    [ObservableProperty] private bool _simulation;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = L.T("Prêt", "Ready");
    /// <summary>Tag de l'onglet affiché (page, versions, galerie…).</summary>
    [ObservableProperty] private string? _currentTab;
    private bool _tabRefreshPending;
    [ObservableProperty] private string _journal = "";

    public MainViewModel()
    {
        S.Http.Log += line => Application.Current.Dispatcher.Invoke(() => Log(line));
        Accounts = new AccountsViewModel(this);
        Settings = new SettingsViewModel(this);
        Page = new PageViewModel(this);
        Versions = new VersionsViewModel(this);
        Gallery = new GalleryViewModel(this);
        Stats = new StatsViewModel(this);

        foreach (var folder in S.Projects.Folders.Where(Directory.Exists))
            Mods.Add(new ModItem(new ModPage(folder)));
        SelectedMod = Mods.FirstOrDefault();
    }

    partial void OnSimulationChanged(bool value)
    {
        S.Http.Simulation = value;
        Log(value ? L.T("Mode simulation : rien ne sera envoyé, les requêtes sont seulement affichées ici.", "Simulation mode: nothing will be sent, requests are only shown here.") : L.T("Mode simulation désactivé.", "Simulation mode off."));
    }

    partial void OnSelectedModChanged(ModItem? value)
    {
        Settings.Load(value?.Page);
        Page.Load(value?.Page);
        Versions.Load(value?.Page);
        Gallery.Load(value?.Page);
        Stats.Load(value?.Page);
        RefreshCurrentTab();
    }

    partial void OnCurrentTabChanged(string? value) => RefreshCurrentTab();

    partial void OnBusyChanged(bool value)
    {
        if (!value && _tabRefreshPending) RefreshCurrentTab();
    }

    /// <summary>Versions, Galerie et Stats se relisent en ligne dès qu'on arrive dessus (après l'opération en cours s'il y en a une).</summary>
    private void RefreshCurrentTab()
    {
        if (ScreenshotMode.Folder is not null || ProbeMode.Folder is not null || SelectedMod is null) return;
        var command = CurrentTab switch
        {
            "versions" => Versions.RefreshCommand,
            "galerie" => Gallery.RefreshCommand,
            "stats" => Stats.RefreshCommand,
            _ => null,
        };
        _tabRefreshPending = command is not null && Busy;
        if (command is not null && !Busy) command.Execute(null);
    }

    public void Log(string line) => Journal += $"[{DateTime.Now:HH:mm:ss}] {line}\n";

    /// <summary>Lance une opération : indicateur occupé, erreurs affichées et journalisées.</summary>
    public async Task RunAsync(string what, Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        Status = what + "…";
        try
        {
            await action();
            Status = what + L.T(" : terminé", ": done");
        }
        catch (Exception e)
        {
            Status = what + L.T(" : échec", ": failed");
            Log(L.T($"ÉCHEC {what} : {e.Message}", $"FAILED {what}: {e.Message}"));
            MessageBox.Show(Application.Current.MainWindow!, e.Message, what, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Busy = false;
        }
    }

    public bool Confirm(string title, string message)
    {
        if (Simulation) message += L.T("\n\n(Mode simulation : rien ne sera réellement envoyé.)", "\n\n(Simulation mode: nothing will actually be sent.)");
        return MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    // ---------- Liste des mods ----------

    [RelayCommand]
    private void AddMod()
    {
        var dialog = new OpenFolderDialog { Title = L.T("Dossier du mod (celui qui contient gradle.properties)", "Mod folder (the one containing gradle.properties)") };
        if (dialog.ShowDialog() != true) return;
        var folder = dialog.FolderName;
        if (!File.Exists(Path.Combine(folder, "gradle.properties")) &&
            MessageBox.Show(L.T("Pas de gradle.properties dans ce dossier. L'ajouter quand même ?", "No gradle.properties in this folder. Add it anyway?"), L.T("Ajouter un mod", "Add a mod"), MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        S.Projects.Add(folder);
        var existing = Mods.FirstOrDefault(m => string.Equals(m.Folder, Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new ModItem(new ModPage(Path.GetFullPath(folder)));
            Mods.Add(existing);
        }
        SelectedMod = existing;
    }

    [RelayCommand]
    private void RemoveMod()
    {
        if (SelectedMod is not { } mod) return;
        if (MessageBox.Show(L.T($"Retirer « {mod.Name} » de la liste ? (Rien n'est supprimé sur le disque ni sur les sites.)", $"Remove “{mod.Name}” from the list? (Nothing is deleted on disk or on the sites.)"),
                L.T("Retirer", "Remove"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        S.Projects.Remove(mod.Folder);
        Mods.Remove(mod);
        SelectedMod = Mods.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenModFolder()
    {
        if (SelectedMod is { } mod) Process.Start("explorer.exe", mod.Folder);
    }

    [RelayCommand]
    private void OpenModrinthPage()
    {
        if (SelectedMod?.Page.ModrinthProject is { Length: > 0 } slug) OpenUrl($"https://modrinth.com/mod/{slug}");
        else Log(L.T("Pas de projet Modrinth dans les réglages.", "No Modrinth project in the settings."));
    }

    [RelayCommand]
    private void OpenCurseForgePage()
    {
        if (SelectedMod?.Page.CurseForgeProjectId is > 0 and var id) OpenUrl($"https://www.curseforge.com/projects/{id}");
        else Log(L.T("Pas de projet CurseForge dans les réglages.", "No CurseForge project in the settings."));
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    [RelayCommand]
    private void ClearJournal() => Journal = "";
}
